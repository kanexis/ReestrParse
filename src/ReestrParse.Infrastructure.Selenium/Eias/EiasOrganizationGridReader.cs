using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using OpenQA.Selenium;
using ReestrParse.Application.Catalog;
using ReestrParse.Domain.Catalog;
using ReestrParse.Domain.Organizations;

namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Читает весь актуальный ASPxGridView2 постранично.
/// Строки берутся из outerHTML только текущей отрисованной таблицы, а количество
/// страниц — из текущего отрисованного pager. Старые скрытые DevExpress-узлы
/// после фильтрации не участвуют в расчётах.
/// </summary>
internal static class EiasOrganizationGridReader
{
    public static IReadOnlyList<OrganizationReference> ReadAllPages(
        IWebDriver driver,
        RegionOption region,
        SphereOption sphere,
        IProgress<CatalogProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var collected = new Dictionary<string, OrganizationReference>(StringComparer.OrdinalIgnoreCase);

        var firstPage = ReadCurrentPage(driver, region, sphere, cancellationToken);
        AddPage(collected, firstPage.Organizations);
        Report(progress, firstPage, collected.Count);

        var totalPages = firstPage.TotalPages;
        var totalRows = firstPage.TotalRows;
        var lastPageRead = firstPage.CurrentPage;

        for (var targetPage = firstPage.CurrentPage + 1;
             targetPage <= totalPages;
             targetPage++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Перед каждым переходом перепроверяем именно живой видимый pager.
            // Если после фильтра сайт показывает только 3 страницы, попытки 4..8
            // физически не будут запускаться даже при наличии старого скрытого pager.
            var liveBefore = EiasLivePager.Read(driver);
            totalPages = Math.Min(totalPages, liveBefore.TotalPages);

            if (targetPage > totalPages)
                break;

            progress?.Report(new CatalogProgress(
                "Пагинация",
                targetPage,
                collected.Count,
                $"Переходим на страницу {targetPage} из {totalPages}..."));

            EiasPagerNavigator.GoToPage(driver, targetPage, cancellationToken);

            var page = ReadCurrentPage(driver, region, sphere, cancellationToken);

            if (page.CurrentPage != targetPage)
            {
                throw new InvalidOperationException(
                    $"Ожидалась страница {targetPage}, но актуальный pager показывает {page.CurrentPage}. " +
                    $"Состояние pager: {EiasLivePager.Read(driver).Summary}");
            }

            // Состояние pager может уточниться после callback.
            totalPages = Math.Min(totalPages, page.TotalPages);
            if (page.TotalRows > 0)
                totalRows = page.TotalRows;

            AddPage(collected, page.Organizations);
            Report(progress, page, collected.Count);
            lastPageRead = page.CurrentPage;
        }

        progress?.Report(new CatalogProgress(
            "Каталог собран",
            lastPageRead,
            collected.Count,
            totalRows > 0
                ? $"Получено {collected.Count} уникальных организаций из {totalRows} строк сайта. " +
                  $"Пройдено страниц: {lastPageRead} из {totalPages}."
                : $"Получено {collected.Count} уникальных организаций. " +
                  $"Пройдено страниц: {lastPageRead} из {totalPages}."));

        return collected.Values
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(x => x.Inn, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static PageResult ReadCurrentPage(
        IWebDriver driver,
        RegionOption region,
        SphereOption sphere,
        CancellationToken cancellationToken)
    {
        var pager = EiasLivePager.Read(driver);
        var rowMetadata = EiasOrganizationRowMetadataReader.Read(driver, region, sphere);
        var selectedFormValue = EiasOrganizationRowMetadataReader.ReadSelectedFormValue(driver);
        var tableHtml = CaptureCurrentRenderedTableHtml(driver, cancellationToken);

        var parser = new HtmlParser();
        var document = parser.ParseDocument(tableHtml);

        var rowElements = document.QuerySelectorAll(
            "tr[id^='ASPxGridView2_DXDataRow']");

        if (rowElements.Length == 0)
        {
            throw new InvalidOperationException(
                "В актуальной отрисованной таблице не найдены строки " +
                "tr[id^='ASPxGridView2_DXDataRow'].");
        }

        var currentPage = Math.Max(1, pager.CurrentPage);
        var totalPages = Math.Max(1, pager.TotalPages);
        var totalRows = pager.TotalRows > 0 ? pager.TotalRows : rowElements.Length;

        var organizations = new List<OrganizationReference>(rowElements.Length);

        foreach (var row in rowElements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cells = row.Children
                .Where(x =>
                    string.Equals(x.LocalName, "td", StringComparison.OrdinalIgnoreCase) &&
                    x.ClassList.Contains("dxgv"))
                .ToArray();

            if (cells.Length < 3)
                continue;

            var name = Normalize(cells[0].TextContent);
            var inn = Normalize(cells[1].TextContent);
            var kpp = Normalize(cells[2].TextContent);

            if (string.IsNullOrWhiteSpace(name))
                continue;

            var localIndex = ReadLocalRowIndex(row, organizations.Count);
            rowMetadata.TryGetValue(localIndex, out var metadata);

            organizations.Add(new OrganizationReference(
                ExternalId: !string.IsNullOrWhiteSpace(metadata?.OrganizationId)
                    ? metadata.OrganizationId
                    : !string.IsNullOrWhiteSpace(inn)
                        ? inn
                        : $"{currentPage}:{organizations.Count + 1}:{name}",
                Name: name,
                Inn: inn,
                Kpp: kpp,
                RegionName: region.Name,
                SphereName: sphere.Name,
                DetailUrl: metadata?.DetailUrl,
                SourcePage: currentPage,
                OrganizationId: metadata?.OrganizationId ?? string.Empty,
                RegionId: region.ExternalId,
                SphereId: sphere.ExternalId,
                FormValue: selectedFormValue));
        }

        if (organizations.Count == 0)
        {
            throw new InvalidOperationException(
                $"Страница {currentPage} найдена, но из неё не удалось извлечь Организация/ИНН/КПП.");
        }

        return new PageResult(
            currentPage,
            totalPages,
            totalRows,
            organizations);
    }


    private static int ReadLocalRowIndex(IElement row, int fallbackIndex)
    {
        var id = row.Id ?? string.Empty;
        const string marker = "DXDataRow";
        var markerIndex = id.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);

        if (markerIndex < 0)
            return fallbackIndex;

        var value = id[(markerIndex + marker.Length)..];
        return int.TryParse(value, out var index) ? index : fallbackIndex;
    }

    private static string CaptureCurrentRenderedTableHtml(
        IWebDriver driver,
        CancellationToken cancellationToken)
    {
        WebDriverException? lastException = null;

        for (var attempt = 1; attempt <= 12; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                SafeDefaultContent(driver);

                var html = ((IJavaScriptExecutor)driver).ExecuteScript("""
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

                    const rows = table.querySelectorAll(
                        "tr[id^='ASPxGridView2_DXDataRow']"
                    );

                    return rows.length > 0 ? table.outerHTML : '';
                    """)?.ToString();

                if (!string.IsNullOrWhiteSpace(html))
                    return html;
            }
            catch (WebDriverException ex)
            {
                lastException = ex;
            }

            Thread.Sleep(150);
        }

        throw new InvalidOperationException(
            "Не удалось получить HTML актуальной отрисованной таблицы ASPxGridView2.",
            lastException);
    }

    private static void AddPage(
        IDictionary<string, OrganizationReference> target,
        IEnumerable<OrganizationReference> organizations)
    {
        foreach (var organization in organizations)
        {
            var key = string.Join("|", organization.Inn, organization.Kpp, organization.Name);
            target.TryAdd(key, organization);
        }
    }

    private static void Report(
        IProgress<CatalogProgress>? progress,
        PageResult page,
        int collectedCount)
    {
        progress?.Report(new CatalogProgress(
            "Чтение организаций",
            page.CurrentPage,
            collectedCount,
            $"Страница {page.CurrentPage} из {page.TotalPages}: " +
            $"прочитано {page.Organizations.Count}, всего собрано {collectedCount}" +
            (page.TotalRows > 0 ? $" из {page.TotalRows}." : ".")));
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static void SafeDefaultContent(IWebDriver driver)
    {
        try
        {
            driver.SwitchTo().DefaultContent();
        }
        catch (NoSuchFrameException)
        {
        }
        catch (StaleElementReferenceException)
        {
        }
    }

    private sealed record PageResult(
        int CurrentPage,
        int TotalPages,
        int TotalRows,
        IReadOnlyList<OrganizationReference> Organizations);
}
