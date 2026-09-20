using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace ReestrParse.Infrastructure.Selenium.Eias.Details;

/// <summary>
/// Читает таблицу опубликованных форм в карточке организации.
/// Вместо клика по jQuery dialog извлекает прямые TemplatePrinter URL из
/// onclick="openTemplateDialog(...)". Exact 4.1.1 имеет высший приоритет, но
/// дополнительно возвращаются другие опубликованные формы — они используются как
/// fallback, если workbook другой строки всё равно содержит лист 4.1.1/1.0.1.
/// </summary>
internal static partial class EiasOrganizationPageReader
{
    public static IReadOnlyList<EiasTemplateCandidate> ExtractTemplateCandidates(
        string html,
        Uri pageUri)
    {
        if (string.IsNullOrWhiteSpace(html))
            throw new InvalidOperationException("Карточка организации вернула пустой HTML.");

        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        var candidates = new List<EiasTemplateCandidate>();

        var rows = document.QuerySelectorAll(
            "tr[id^='ASPxGridViewDet_DXDataRow']");

        foreach (var row in rows)
        {
            var link = row.QuerySelector("a.get_template")
                ?? row.QuerySelector("a[onclick*='openTemplateDialog']");

            if (link is null)
                continue;

            var onclick = WebUtility.HtmlDecode(link.GetAttribute("onclick") ?? string.Empty);
            var match = TemplateUrlRegex().Match(onclick);
            if (!match.Success)
                continue;

            var rawUrl = WebUtility.HtmlDecode(match.Groups["url"].Value).Trim();
            var url = NormalizeTemplateUrl(rawUrl, pageUri);
            if (string.IsNullOrWhiteSpace(url))
                continue;

            var cells = row.Children
                .Where(x => string.Equals(x.LocalName, "td", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var formNumber = cells.Length > 3 ? Normalize(cells[3].TextContent) : string.Empty;
            var formName = cells.Length > 4 ? Normalize(cells[4].TextContent) : string.Empty;
            var publicationDate = cells.Length > 0 ? Normalize(cells[^1].TextContent) : string.Empty;

            candidates.Add(new EiasTemplateCandidate(
                formNumber,
                formName,
                url,
                publicationDate,
                Priority(formNumber, formName)));
        }

        // На отдельных карточках markup строки может отличаться. Не теряем ссылки,
        // которые присутствуют вне ожидаемой структуры DevExpress-row.
        foreach (var link in document.QuerySelectorAll("a.get_template, a[onclick*='openTemplateDialog']"))
        {
            var onclick = WebUtility.HtmlDecode(link.GetAttribute("onclick") ?? string.Empty);
            var match = TemplateUrlRegex().Match(onclick);
            if (!match.Success)
                continue;

            var url = NormalizeTemplateUrl(
                WebUtility.HtmlDecode(match.Groups["url"].Value).Trim(),
                pageUri);

            if (string.IsNullOrWhiteSpace(url) ||
                candidates.Any(x => string.Equals(x.TemplateUrl, url, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            candidates.Add(new EiasTemplateCandidate(
                string.Empty,
                "Опубликованная форма (структура строки не распознана)",
                url,
                string.Empty,
                50));
        }

        return candidates
            .GroupBy(x => x.TemplateUrl, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.OrderBy(c => c.Priority).First())
            .OrderBy(x => x.Priority)
            .ThenByDescending(x => ParseDate(x.PublicationDate))
            .ToArray();
    }

    public static string ExtractForm411TemplateUrl(string html, Uri pageUri)
    {
        var candidate = ExtractTemplateCandidates(html, pageUri)
            .FirstOrDefault(x => x.IsForm411);

        return candidate?.TemplateUrl
            ?? throw new InvalidOperationException(
                "В карточке организации не найдена опубликованная форма 4.1.1 «Общая информация об организации».");
    }

    private static int Priority(string formNumber, string formName)
    {
        if (string.Equals(formNumber, "4.1.1", StringComparison.OrdinalIgnoreCase) &&
            formName.Contains("Общая информация об организации", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (string.Equals(formNumber, "4.1.1", StringComparison.OrdinalIgnoreCase))
            return 1;

        if (formNumber.StartsWith("1", StringComparison.OrdinalIgnoreCase) ||
            formName.Contains("Основные параметры", StringComparison.OrdinalIgnoreCase))
        {
            return 10;
        }

        return 20;
    }

    private static string? NormalizeTemplateUrl(string rawUrl, Uri pageUri)
    {
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        return Uri.TryCreate(pageUri, rawUrl, out var relative)
            ? relative.ToString()
            : null;
    }

    private static DateTime ParseDate(string value)
        => DateTime.TryParse(value, out var parsed) ? parsed : DateTime.MinValue;

    private static string Normalize(string? value)
        => EiasFormTableReader.Normalize(value);

    [GeneratedRegex("openTemplateDialog\\(\\s*['\"](?<url>[^'\"]+)['\"]", RegexOptions.IgnoreCase)]
    private static partial Regex TemplateUrlRegex();
}

internal sealed record EiasTemplateCandidate(
    string FormNumber,
    string FormName,
    string TemplateUrl,
    string PublicationDate,
    int Priority)
{
    public bool IsForm411 =>
        string.Equals(FormNumber, "4.1.1", StringComparison.OrdinalIgnoreCase) ||
        FormName.Contains("Общая информация об организации", StringComparison.OrdinalIgnoreCase);

    public bool IsForm101 =>
        string.Equals(FormNumber, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(FormNumber, "1.0.1", StringComparison.OrdinalIgnoreCase) ||
        FormName.Contains("Основные параметры раскрываемой информации", StringComparison.OrdinalIgnoreCase);

    public string Caption => string.IsNullOrWhiteSpace(FormNumber)
        ? FormName
        : $"{FormNumber} — {FormName}";
}
