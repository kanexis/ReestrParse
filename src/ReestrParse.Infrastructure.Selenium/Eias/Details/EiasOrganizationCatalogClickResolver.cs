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
    private const int MaxOrganizationOpenAttempts = 5;
    private static readonly TimeSpan CardNavigationTimeout = TimeSpan.FromSeconds(20);

    private static readonly string[] OrganizationPageMarkers =
    [
        "/Discl/PublicDisclosureInfoOrg.aspx",
        "/Discl/PublicDisclosureInfo.aspx"
    ];
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

        WebDriverException? lastWebDriverException = null;

        for (var attempt = 1; attempt <= MaxOrganizationOpenAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (attempt > 1)
            {
                telemetry.Warning(
                    ParserPipelineStage.DetailsNavigation,
                    "Organization card open retry",
                    $"Worker #{workerId}: повторная попытка {attempt}/{MaxOrganizationOpenAttempts} открыть карточку " +
                    $"«{organization.Name}». Возвращаемся в каталог, заново применяем фильтры и ищем строку.",
                    workerId: workerId,
                    page: organization.SourcePage,
                    itemIndex: itemIndex,
                    totalItems: totalItems,
                    organizationName: organization.Name,
                    inn: organization.Inn,
                    error: lastWebDriverException?.Message,
                    code: "CATALOG_ORG_OPEN_RETRY",
                    dataSource: "catalog");
            }

            try
            {
                return ResolveAndOpenAttempt(
                    browser,
                    organization,
                    catalogUrl,
                    telemetry,
                    workerId,
                    itemIndex,
                    totalItems,
                    attempt,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (WebDriverException ex) when (attempt < MaxOrganizationOpenAttempts)
            {
                lastWebDriverException = ex;

                telemetry.Warning(
                    ParserPipelineStage.DetailsNavigation,
                    "Organization card open attempt failed",
                    $"Worker #{workerId}: попытка {attempt}/{MaxOrganizationOpenAttempts} открыть карточку " +
                    $"«{organization.Name}» не удалась. Следующая попытка начнётся с чистого каталога и повторного применения фильтров.",
                    workerId: workerId,
                    page: organization.SourcePage,
                    itemIndex: itemIndex,
                    totalItems: totalItems,
                    organizationName: organization.Name,
                    inn: organization.Inn,
                    error: ex.Message,
                    code: "CATALOG_ORG_OPEN_ATTEMPT_FAILED",
                    dataSource: "catalog");

                RecoverBeforeRetry(browser, catalogUrl, cancellationToken);
            }
        }

        // Фактически недостижимо: на пятой попытке exception не перехватывается.
        throw lastWebDriverException ?? new WebDriverException(
            $"Не удалось открыть карточку организации «{organization.Name}» после {MaxOrganizationOpenAttempts} попыток.");
    }

    private static string ResolveAndOpenAttempt(
        SeleniumWorkerBrowser browser,
        OrganizationReference organization,
        string catalogUrl,
        IParserTelemetry telemetry,
        int workerId,
        int itemIndex,
        int totalItems,
        int attempt,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Каждая попытка начинается с чистого входа в каталог. Не используем DOM,
        // оставшийся после предыдущего зависшего DevExpress callback.
        browser.Driver.Navigate().GoToUrl(catalogUrl);
        EiasSearchExecutor.WaitUntilReady(browser.Driver, browser.Wait);

        var filteredState = EiasSearchExecutor.SubmitFiltersAndWait(
            browser.Driver,
            browser.Wait,
            cancellationToken);

        telemetry.Success(
            ParserPipelineStage.DetailsNavigation,
            "Worker filters submitted",
            $"Worker #{workerId}: попытка {attempt}/{MaxOrganizationOpenAttempts}; фильтры WARM + " +
            $"«Общая информация об организации» применены; grid: {filteredState.Summary}.",
            workerId: workerId,
            page: 1,
            itemIndex: itemIndex,
            totalItems: totalItems,
            organizationName: organization.Name,
            inn: organization.Inn,
            code: "WORKER_FILTERS_SUBMITTED",
            dataSource: "catalog");

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
                NavigateToPageWithRecovery(
                    browser,
                    catalogUrl,
                    page,
                    organization,
                    telemetry,
                    workerId,
                    itemIndex,
                    totalItems,
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
                    ? $"Worker #{workerId}: попытка {attempt}/{MaxOrganizationOpenAttempts}; строка не найдена на странице {page}; продолжаем поиск."
                    : $"Worker #{workerId}: попытка {attempt}/{MaxOrganizationOpenAttempts}; строка найдена на странице {page} по стратегии {match.Strategy}.",
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
                $"Попытка {attempt}/{MaxOrganizationOpenAttempts}. Проверены страницы: {string.Join(", ", scannedPages)}.");
        }

        var beforeUrl = browser.Driver.Url;
        ClickMatchedRow(browser, match.RowId, cancellationToken);

        var navigationWait = new WebDriverWait(browser.Driver, CardNavigationTimeout)
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
                    if (!IsRecognizedOrganizationUrl(url))
                        return false;

                    var urlChanged = !string.Equals(url, beforeUrl, StringComparison.OrdinalIgnoreCase);
                    var cardDom = EiasPageWaiter.ExecuteBool(d, """
                        return Boolean(
                            document.querySelector("[id^='ASPxGridViewDet']") ||
                            document.querySelector("a.get_template") ||
                            document.querySelector("a[onclick*='openTemplateDialog']")
                        );
                        """);

                    var explicitOrgRoute = url.Contains(
                        "/Discl/PublicDisclosureInfoOrg.aspx",
                        StringComparison.OrdinalIgnoreCase);

                    return explicitOrgRoute ? (urlChanged || cardDom) : cardDom;
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
                $"но браузер не перешёл в карточку организации за {CardNavigationTimeout.TotalSeconds:0} секунд. " +
                $"Попытка {attempt}/{MaxOrganizationOpenAttempts}.", ex);
        }

        WaitForDocument(browser, cancellationToken);

        var resolvedUrl = browser.Driver.Url ?? string.Empty;
        if (!IsRecognizedOrganizationUrl(resolvedUrl))
        {
            throw new WebDriverException(
                $"После клика по «{organization.Name}» открыт URL, не похожий на карточку ЕИАС: {resolvedUrl}. " +
                $"Попытка {attempt}/{MaxOrganizationOpenAttempts}.");
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

        telemetry.Success(
            ParserPipelineStage.DetailsNavigation,
            "Organization card opened",
            $"Worker #{workerId}: карточка «{organization.Name}» открыта с попытки {attempt}/{MaxOrganizationOpenAttempts}.",
            workerId: workerId,
            page: resolvedPage,
            itemIndex: itemIndex,
            totalItems: totalItems,
            organizationName: organization.Name,
            inn: organization.Inn,
            code: "CATALOG_ORG_OPENED",
            dataSource: "catalog");

        return resolvedUrl;
    }

    private static void RecoverBeforeRetry(
        SeleniumWorkerBrowser browser,
        string catalogUrl,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            browser.Driver.SwitchTo().DefaultContent();
        }
        catch (WebDriverException)
        {
        }

        try
        {
            // Прямой возврат на catalog URL надёжнее Back(): история может содержать
            // несколько DevExpress postback-состояний и TemplatePrinter переходов.
            browser.Driver.Navigate().GoToUrl(catalogUrl);
            EiasSearchExecutor.WaitUntilReady(browser.Driver, browser.Wait);
        }
        catch (WebDriverException)
        {
            // Следующая ResolveAndOpenAttempt всё равно выполнит GoToUrl(catalogUrl).
            // Здесь best-effort очистка состояния, чтобы не маскировать исходную ошибку.
            try
            {
                browser.Driver.Navigate().Refresh();
            }
            catch (WebDriverException)
            {
            }
        }

        Thread.Sleep(350);
    }

    private static void NavigateToPageWithRecovery(
        SeleniumWorkerBrowser browser,
        string catalogUrl,
        int targetPage,
        OrganizationReference organization,
        IParserTelemetry telemetry,
        int workerId,
        int itemIndex,
        int totalItems,
        CancellationToken cancellationToken)
    {
        try
        {
            EiasPagerNavigator.GoToPage(browser.Driver, targetPage, cancellationToken);
            return;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WebDriverException ex)
        {
            telemetry.Warning(
                ParserPipelineStage.DetailsNavigation,
                "Catalog page navigation retry",
                $"Worker #{workerId}: DevExpress не подтвердил переход на страницу {targetPage}. " +
                "Пересоздаём отфильтрованное состояние каталога и повторяем переход один раз.",
                workerId: workerId,
                page: targetPage,
                itemIndex: itemIndex,
                totalItems: totalItems,
                organizationName: organization.Name,
                inn: organization.Inn,
                error: ex.Message,
                code: "CATALOG_PAGE_RETRY",
                dataSource: "catalog");
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Иногда ASPxGridView callback зависает именно внутри одной worker-session:
        // pager остаётся на page 1, хотя click уже был отправлен. Полный возврат к
        // catalog URL + повторное применение тех же фильтров дешевле и надёжнее,
        // чем продолжать работать с потенциально подвисшим callback state.
        browser.Driver.Navigate().GoToUrl(catalogUrl);
        EiasSearchExecutor.WaitUntilReady(browser.Driver, browser.Wait);
        var state = EiasSearchExecutor.SubmitFiltersAndWait(
            browser.Driver,
            browser.Wait,
            cancellationToken);
        WaitForCatalog(browser, cancellationToken);

        if (targetPage > state.RowCount)
        {
            throw new InvalidOperationException(
                $"После восстановления каталога страница {targetPage} отсутствует. {state.Summary}");
        }

        if (targetPage != 1)
            EiasPagerNavigator.GoToPage(browser.Driver, targetPage, cancellationToken);
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


    private static bool IsRecognizedOrganizationUrl(string? url)
        => !string.IsNullOrWhiteSpace(url) &&
           OrganizationPageMarkers.Any(marker =>
               url.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private sealed record RawMatchResult(string RowId, string Strategy);
    private sealed record MatchResult(string RowId, string Strategy);
}
