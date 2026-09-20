using System.Text.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using ReestrParse.Application.Monitoring;
using ReestrParse.Domain.Organizations;
using ReestrParse.Infrastructure.Selenium.Browser;

namespace ReestrParse.Infrastructure.Selenium.Eias.Details;

/// <summary>
/// Fallback для строк каталога без orgId/DetailUrl.
/// SourcePage используется как подсказка, а не как жёсткое условие: если порядок
/// DevExpress отличается в новой worker-session, resolver адаптивно проверяет другие
/// страницы и ищет строку по Name+INN+KPP, затем INN+KPP, затем уникальному INN.
/// </summary>
internal static class EiasOrganizationCatalogClickResolver
{
    private const string OrganizationPageMarker = "/Discl/PublicDisclosureInfoOrg.aspx";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string ResolveAndOpen(
        SeleniumWorkerBrowser browser,
        OrganizationReference organization,
        IParserTelemetry telemetry,
        int workerId,
        int itemIndex,
        int totalItems,
        CancellationToken cancellationToken)
    {
        var catalogUrl = EiasCatalogUrlBuilder.Build(organization)
            ?? throw new InvalidOperationException(
                "Недостаточно данных для fallback-перехода через каталог: отсутствуют region/sphere/form.");

        cancellationToken.ThrowIfCancellationRequested();
        browser.Driver.Navigate().GoToUrl(catalogUrl);
        WaitForCatalog(browser, cancellationToken);

        var pager = EiasLivePager.Read(browser.Driver);
        var pageOrder = BuildPageOrder(
            Math.Clamp(organization.SourcePage, 1, Math.Max(1, pager.TotalPages)),
            Math.Max(1, pager.TotalPages));

        var scannedPages = new List<int>();
        MatchResult? match = null;
        int resolvedPage = 0;

        foreach (var page in pageOrder)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scannedPages.Add(page);

            var current = EiasLivePager.Read(browser.Driver).CurrentPage;
            if (current != page)
            {
                EiasPagerNavigator.GoToPage(
                    browser.Driver,
                    page,
                    cancellationToken);
            }

            WaitForCatalog(browser, cancellationToken);

            var scanSw = System.Diagnostics.Stopwatch.StartNew();
            match = WaitForMatchingRow(
                browser,
                organization,
                TimeSpan.FromSeconds(6),
                cancellationToken);
            scanSw.Stop();

            telemetry.Info(
                ParserPipelineStage.DetailsNavigation,
                "Catalog fallback page scanned",
                match is null
                    ? $"Worker #{workerId}: строка не найдена на странице {page}; продолжаем поиск."
                    : $"Worker #{workerId}: строка найдена на странице {page} по стратегии {match.Strategy}.",
                scanSw.Elapsed,
                workerId,
                page: page,
                itemIndex: itemIndex,
                totalItems: totalItems,
                organizationName: organization.Name,
                inn: organization.Inn,
                code: match is null ? "CATALOG_ROW_NOT_ON_PAGE" : "CATALOG_ROW_MATCHED",
                dataSource: "catalog");

            if (match is not null)
            {
                resolvedPage = page;
                break;
            }
        }

        if (match is null)
        {
            throw new NoSuchElementException(
                $"Не найдена строка организации «{organization.Name}» " +
                $"(ИНН {organization.Inn}, КПП {organization.Kpp}). " +
                $"Проверены страницы: {string.Join(", ", scannedPages)}.");
        }

        var beforeUrl = browser.Driver.Url;
        ClickMatchedRow(browser, match.RowId, cancellationToken);

        var navigationWait = new WebDriverWait(browser.Driver, TimeSpan.FromSeconds(45))
        {
            PollingInterval = TimeSpan.FromMilliseconds(250)
        };

        try
        {
            navigationWait.Until(d =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var url = d.Url ?? string.Empty;
                    return url.Contains(OrganizationPageMarker, StringComparison.OrdinalIgnoreCase) ||
                           (!string.IsNullOrWhiteSpace(url) &&
                            !string.Equals(url, beforeUrl, StringComparison.OrdinalIgnoreCase));
                }
                catch (WebDriverException)
                {
                    return false;
                }
            });
        }
        catch (WebDriverTimeoutException ex)
        {
            throw new WebDriverTimeoutException(
                $"Строка организации «{organization.Name}» была нажата на странице {resolvedPage}, " +
                "но браузер не перешёл в карточку организации.", ex);
        }

        WaitForDocument(browser, cancellationToken);

        var resolvedUrl = browser.Driver.Url ?? string.Empty;
        if (!resolvedUrl.Contains(OrganizationPageMarker, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"После клика по «{organization.Name}» открыт неожиданный URL: {resolvedUrl}");
        }

        if (resolvedPage != organization.SourcePage)
        {
            telemetry.Warning(
                ParserPipelineStage.DetailsNavigation,
                "Catalog source page corrected",
                $"SourcePage={organization.SourcePage}, фактическая строка найдена на странице {resolvedPage}. " +
                "SourcePage используется только как hint, потому что порядок DevExpress может отличаться между сессиями.",
                workerId: workerId,
                page: resolvedPage,
                itemIndex: itemIndex,
                totalItems: totalItems,
                organizationName: organization.Name,
                inn: organization.Inn,
                code: "CATALOG_SOURCE_PAGE_MISMATCH",
                dataSource: "catalog");
        }

        return resolvedUrl;
    }

    private static IReadOnlyList<int> BuildPageOrder(int preferredPage, int totalPages)
    {
        var pages = new List<int> { preferredPage };

        for (var distance = 1; pages.Count < totalPages; distance++)
        {
            var left = preferredPage - distance;
            var right = preferredPage + distance;

            if (left >= 1)
                pages.Add(left);

            if (right <= totalPages)
                pages.Add(right);
        }

        return pages.Distinct().ToArray();
    }

    private static MatchResult? WaitForMatchingRow(
        SeleniumWorkerBrowser browser,
        OrganizationReference organization,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var wait = new WebDriverWait(browser.Driver, timeout)
        {
            PollingInterval = TimeSpan.FromMilliseconds(250)
        };

        try
        {
            return wait.Until(d =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    d.SwitchTo().DefaultContent();
                    return FindMatchingRow(d, organization);
                }
                catch (WebDriverException)
                {
                    return null;
                }
            });
        }
        catch (WebDriverTimeoutException)
        {
            return null;
        }
    }

    private static MatchResult? FindMatchingRow(
        IWebDriver driver,
        OrganizationReference organization)
    {
        var json = ((IJavaScriptExecutor)driver).ExecuteScript("""
            const expectedName = arguments[0];
            const expectedInn = arguments[1];
            const expectedKpp = arguments[2];

            const normalize = value => (value || '')
                .replace(/\u00a0/g, ' ')
                .replace(/[«»„“”]/g, '"')
                .replace(/\s+/g, ' ')
                .trim()
                .toLowerCase();

            const rendered = el => {
                if (!el) return false;
                const s = getComputedStyle(el);
                return s.display !== 'none' &&
                       s.visibility !== 'hidden' &&
                       s.opacity !== '0' &&
                       el.getClientRects().length > 0;
            };

            const tables = Array.from(document.querySelectorAll(
                "[id='ASPxGridView2_DXMainTable']"
            ));
            const table = tables.find(rendered) || tables[tables.length - 1];
            if (!table) return '';

            const rows = Array.from(table.querySelectorAll(
                "tr[id^='ASPxGridView2_DXDataRow']"
            ));

            const mapped = rows.map(row => {
                const cells = Array.from(row.querySelectorAll(':scope > td.dxgv'));
                return {
                    row,
                    name: normalize(cells[0]?.textContent),
                    inn: normalize(cells[1]?.textContent),
                    kpp: normalize(cells[2]?.textContent)
                };
            }).filter(x => x.inn);

            const name = normalize(expectedName);
            const inn = normalize(expectedInn);
            const kpp = normalize(expectedKpp);

            let found = mapped.find(x =>
                x.name === name && x.inn === inn && x.kpp === kpp);
            if (found) return JSON.stringify({ rowId: found.row.id || '', strategy: 'name+inn+kpp' });

            found = mapped.find(x => x.inn === inn && x.kpp === kpp);
            if (found) return JSON.stringify({ rowId: found.row.id || '', strategy: 'inn+kpp' });

            const byInn = mapped.filter(x => x.inn === inn);
            if (byInn.length === 1)
                return JSON.stringify({ rowId: byInn[0].row.id || '', strategy: 'unique-inn' });

            return '';
            """,
            organization.Name,
            organization.Inn,
            organization.Kpp)?.ToString();

        if (string.IsNullOrWhiteSpace(json))
            return null;

        var raw = JsonSerializer.Deserialize<RawMatchResult>(json, JsonOptions);
        return raw is null || string.IsNullOrWhiteSpace(raw.RowId)
            ? null
            : new MatchResult(raw.RowId, raw.Strategy);
    }

    private static void ClickMatchedRow(
        SeleniumWorkerBrowser browser,
        string rowId,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                browser.Driver.SwitchTo().DefaultContent();

                var row = browser.Driver.FindElement(By.Id(rowId));
                var cell = row.FindElements(By.CssSelector(":scope > td.dxgv")).FirstOrDefault()
                    ?? row.FindElements(By.TagName("td")).FirstOrDefault();

                if (cell is null)
                    throw new NoSuchElementException($"В строке {rowId} отсутствует первая ячейка.");

                new Actions(browser.Driver)
                    .ScrollToElement(cell)
                    .MoveToElement(cell)
                    .Pause(TimeSpan.FromMilliseconds(100))
                    .Click(cell)
                    .Perform();

                return;
            }
            catch (WebDriverException ex)
            {
                lastException = ex;
                Thread.Sleep(180);
            }
        }

        throw new WebDriverException(
            $"Не удалось кликнуть найденную строку {rowId} организации.",
            lastException);
    }

    private static void WaitForCatalog(
        SeleniumWorkerBrowser browser,
        CancellationToken cancellationToken)
    {
        browser.Wait.Until(d =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                d.SwitchTo().DefaultContent();

                var ready = string.Equals(
                    ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString(),
                    "complete",
                    StringComparison.OrdinalIgnoreCase);

                if (!ready)
                    return false;

                return Convert.ToBoolean(((IJavaScriptExecutor)d).ExecuteScript("""
                    const visible = el => {
                        if (!el) return false;
                        const s = getComputedStyle(el);
                        return s.display !== 'none' &&
                               s.visibility !== 'hidden' &&
                               s.opacity !== '0' &&
                               el.getClientRects().length > 0;
                    };

                    const tables = Array.from(document.querySelectorAll(
                        "[id='ASPxGridView2_DXMainTable']"
                    ));
                    const table = tables.find(visible) || tables[tables.length - 1];
                    if (!table) return false;

                    const loading = [
                        document.getElementById('ASPxGridView2_LP'),
                        document.getElementById('ASPxGridView2_LD')
                    ].some(visible);

                    if (loading) return false;

                    return table.querySelectorAll(
                        "tr[id^='ASPxGridView2_DXDataRow']"
                    ).length > 0;
                    """));
            }
            catch (WebDriverException)
            {
                return false;
            }
        });
    }

    private static void WaitForDocument(
        SeleniumWorkerBrowser browser,
        CancellationToken cancellationToken)
    {
        browser.Wait.Until(d =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                d.SwitchTo().DefaultContent();
                return string.Equals(
                    ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString(),
                    "complete",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (WebDriverException)
            {
                return false;
            }
        });
    }

    private sealed record RawMatchResult(string RowId, string Strategy);
    private sealed record MatchResult(string RowId, string Strategy);
}
