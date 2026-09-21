using System.Diagnostics;
using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Отдельный этап поиска. Критично не считать старые строки ASPxGridView2 готовым
/// результатом сразу после click(): DevExpress запускает callback асинхронно и несколько
/// сотен миллисекунд в DOM может оставаться предыдущая таблица.
/// </summary>
internal static class EiasSearchExecutor
{
    public static void WaitUntilReady(IWebDriver driver, WebDriverWait wait)
    {
        EiasPageWaiter.WaitForDocumentReady(driver, wait);
        EiasPageWaiter.WaitForSearchPageReady(driver, wait);
    }

    public static EiasSearchSnapshot CaptureState(IWebDriver driver)
    {
        try
        {
            driver.SwitchTo().DefaultContent();
            var json = ((IJavaScriptExecutor)driver).ExecuteScript("""
                const rendered = el => {
                    if (!el) return false;
                    const s = getComputedStyle(el);
                    return s.display !== 'none' && s.visibility !== 'hidden' &&
                           s.opacity !== '0' && el.getClientRects().length > 0;
                };

                const tables = Array.from(document.querySelectorAll("[id='ASPxGridView2_DXMainTable']"));
                const table = tables.find(rendered) || tables[tables.length - 1] || null;
                const rows = table ? Array.from(table.querySelectorAll("tr[id^='ASPxGridView2_DXDataRow']")) : [];
                const rowSignature = rows.slice(0, 5).map(row =>
                    Array.from(row.querySelectorAll(':scope > td.dxgv')).slice(0, 3)
                        .map(td => (td.textContent || '').replace(/\s+/g, ' ').trim())
                        .join('|')
                ).join('||');

                const pagers = Array.from(document.querySelectorAll("[id='ASPxGridView2_DXPagerBottom']"));
                const pager = pagers.find(rendered) || pagers[pagers.length - 1] || null;
                const summary = (pager?.querySelector('.dxp-summary')?.textContent || '')
                    .replace(/\s+/g, ' ').trim();

                const selected = id => Array.from(document.querySelectorAll(`#${id} option:checked`))
                    .map(x => String(x.value || '').trim()).filter(Boolean);

                return JSON.stringify({
                    rowCount: rows.length,
                    rowSignature,
                    summary,
                    documentEpoch: String(window.performance?.timeOrigin || 0),
                    sphereValues: selected('SphereSelect'),
                    formValues: selected('FormSelect')
                });
                """)?.ToString();

            if (string.IsNullOrWhiteSpace(json))
                return EiasSearchSnapshot.Empty;

            var raw = JsonSerializer.Deserialize<RawSearchSnapshot>(json, JsonOptions);
            return raw is null
                ? EiasSearchSnapshot.Empty
                : new EiasSearchSnapshot(
                    raw.RowCount,
                    raw.RowSignature ?? string.Empty,
                    raw.Summary ?? string.Empty,
                    raw.DocumentEpoch ?? string.Empty,
                    raw.SphereValues ?? [],
                    raw.FormValues ?? []);
        }
        catch (WebDriverException)
        {
            return EiasSearchSnapshot.Empty;
        }
        catch (JsonException)
        {
            return EiasSearchSnapshot.Empty;
        }
    }

    public static void ClickSearch(IWebDriver driver, WebDriverWait wait)
    {
        WaitUntilReady(driver, wait);

        var clicked = EiasPageWaiter.ExecuteBool(driver, """
            const button = document.getElementById('searchBtn');
            if (!button) return false;
            button.click();
            return true;
            """);

        if (!clicked)
            throw new NoSuchElementException("Кнопка НАЙТИ (#searchBtn) не найдена на полностью загруженной странице.");
    }

    /// <summary>
    /// Ждёт именно НОВОЕ и стабилизировавшееся состояние grid после #searchBtn.
    /// Старый grid с 381 строкой больше не может пройти wait за 20-30 мс.
    /// </summary>
    public static EiasSearchSnapshot WaitForFirstPage(
        IWebDriver driver,
        EiasSearchSnapshot beforeSearch,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var resultWait = new WebDriverWait(driver, TimeSpan.FromSeconds(60))
        {
            PollingInterval = TimeSpan.FromMilliseconds(250)
        };

        EiasSearchSnapshot? last = null;
        var stableHits = 0;
        var callbackObserved = false;
        var stateChanged = false;

        try
        {
            return resultWait.Until(d =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    d.SwitchTo().DefaultContent();
                    var state = CaptureState(d);
                    var loading = IsGridLoading(d);

                    callbackObserved |= loading;
                    stateChanged |= !state.GridIdentityEquals(beforeSearch);

                    if (loading || state.RowCount <= 0 || !HasExpectedFilters(state))
                    {
                        stableHits = 0;
                        last = state;
                        return null;
                    }

                    // Не разрешаем старому DOM пройти wait сразу после button.click().
                    // Если callback/изменение уже видели, всё равно требуем короткую стабилизацию.
                    var minimumAge = (callbackObserved || stateChanged)
                        ? TimeSpan.FromMilliseconds(700)
                        : TimeSpan.FromMilliseconds(4500);

                    if (started.Elapsed < minimumAge)
                    {
                        stableHits = 0;
                        last = state;
                        return null;
                    }

                    if (last is not null && state.GridIdentityEquals(last))
                        stableHits++;
                    else
                        stableHits = 1;

                    last = state;
                    return stableHits >= 3 ? state : null;
                }
                catch (WebDriverException)
                {
                    stableHits = 0;
                    return null;
                }
            }) ?? throw new WebDriverTimeoutException("Каталог не стабилизировался после поиска.");
        }
        catch (WebDriverTimeoutException ex)
        {
            var diagnostic = SaveDiagnosticPage(driver);
            throw new WebDriverTimeoutException(
                "Кнопка #searchBtn была нажата, но новый отфильтрованный ASPxGridView2 " +
                "не успел стабилизироваться. Старое состояние grid намеренно не принимается. " +
                $"HTML после поиска сохранён: {diagnostic}", ex);
        }
    }

    public static EiasSearchSnapshot SubmitFiltersAndWait(
        IWebDriver driver,
        WebDriverWait wait,
        CancellationToken cancellationToken)
    {
        WaitUntilReady(driver, wait);
        EiasFilterSelector.SelectHeatSupply(driver, wait);
        WaitUntilReady(driver, wait);
        EiasFilterSelector.SelectGeneralOrganizationInfo(driver, wait);
        WaitUntilReady(driver, wait);

        var before = CaptureState(driver);
        ClickSearch(driver, wait);
        return WaitForFirstPage(driver, before, cancellationToken);
    }

    private static bool HasExpectedFilters(EiasSearchSnapshot state)
    {
        var sphereOk = state.SphereValues.Count == 1 &&
            string.Equals(state.SphereValues[0], "WARM", StringComparison.OrdinalIgnoreCase);

        // В ЕИАС "Общая информация об организации" — ОДНА option, value которой
        // содержит сразу несколько form-id через ';'. Нельзя split-ить её и искать
        // отдельную option для каждого токена.
        var formOk = state.FormValues.Count == 1 &&
            (state.FormValues[0].Contains("F_W_O_1", StringComparison.OrdinalIgnoreCase) ||
             state.FormValues[0].Contains("F_W_O_4_1_1", StringComparison.OrdinalIgnoreCase));

        return sphereOk && formOk;
    }

    private static bool IsGridLoading(IWebDriver driver)
        => EiasPageWaiter.ExecuteBool(driver, """
            const visible = el => {
                if (!el) return false;
                const s = getComputedStyle(el);
                return s.display !== 'none' && s.visibility !== 'hidden' &&
                       s.opacity !== '0' && el.getClientRects().length > 0;
            };
            return visible(document.getElementById('ASPxGridView2_LP')) ||
                   visible(document.getElementById('ASPxGridView2_LD'));
            """);

    private static string SaveDiagnosticPage(IWebDriver driver)
    {
        try
        {
            driver.SwitchTo().DefaultContent();
            var directory = Path.Combine(AppContext.BaseDirectory, "diagnostics");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"eias-after-search-{DateTime.Now:yyyyMMdd-HHmmss}.html");
            File.WriteAllText(path, driver.PageSource);
            return path;
        }
        catch (Exception ex)
        {
            return $"не удалось сохранить ({ex.Message})";
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record RawSearchSnapshot(
        int RowCount,
        string? RowSignature,
        string? Summary,
        string? DocumentEpoch,
        string[]? SphereValues,
        string[]? FormValues);
}

internal sealed record EiasSearchSnapshot(
    int RowCount,
    string RowSignature,
    string Summary,
    string DocumentEpoch,
    IReadOnlyList<string> SphereValues,
    IReadOnlyList<string> FormValues)
{
    public static EiasSearchSnapshot Empty { get; } = new(0, string.Empty, string.Empty, string.Empty, [], []);

    public bool GridIdentityEquals(EiasSearchSnapshot other)
        => RowCount == other.RowCount &&
           string.Equals(RowSignature, other.RowSignature, StringComparison.Ordinal) &&
           string.Equals(Summary, other.Summary, StringComparison.Ordinal) &&
           string.Equals(DocumentEpoch, other.DocumentEpoch, StringComparison.Ordinal);
}
