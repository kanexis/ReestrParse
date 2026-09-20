using System.Net;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace ReestrParse.Infrastructure.Selenium.Eias.Details;

/// <summary>
/// Из HTML карточки организации выбирает только форму 4.1.1 и извлекает прямой
/// TemplatePrinter URL из onclick="openTemplateDialog(...)". Модальное окно и iframe
/// для машинного сбора не открываются.
/// </summary>
internal static partial class EiasOrganizationPageReader
{
    public static string ExtractForm411TemplateUrl(string html, Uri pageUri)
    {
        if (string.IsNullOrWhiteSpace(html))
            throw new InvalidOperationException("Карточка организации вернула пустой HTML.");

        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);

        var rows = document.QuerySelectorAll(
            "#ASPxGridViewDet_DXMainTable tr[id^='ASPxGridViewDet_DXDataRow']");

        foreach (var row in rows)
        {
            var cells = row.Children
                .Where(x => string.Equals(x.LocalName, "td", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (cells.Length < 5)
                continue;

            var formNumber = Normalize(cells[3].TextContent);
            var formName = Normalize(cells[4].TextContent);

            if (!string.Equals(formNumber, "4.1.1", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!formName.Contains("Общая информация об организации", StringComparison.OrdinalIgnoreCase))
                continue;

            var link = row.QuerySelector("a.get_template")
                ?? row.QuerySelector("a[onclick*='openTemplateDialog']");

            if (link is null)
                throw new InvalidOperationException("Для формы 4.1.1 не найдена кнопка «Просмотр формы».");

            var onclick = WebUtility.HtmlDecode(link.GetAttribute("onclick") ?? string.Empty);
            var match = TemplateUrlRegex().Match(onclick);

            if (!match.Success)
                throw new InvalidOperationException("Не удалось извлечь TemplatePrinter URL из формы 4.1.1.");

            var value = WebUtility.HtmlDecode(match.Groups["url"].Value).Trim();
            if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
                return absolute.ToString();

            if (Uri.TryCreate(pageUri, value, out var relative))
                return relative.ToString();

            throw new InvalidOperationException($"Некорректный TemplatePrinter URL: {value}");
        }

        throw new InvalidOperationException(
            "В карточке организации не найдена опубликованная форма 4.1.1 «Общая информация об организации».");
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value.Split(
            (char[]?)null,
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    [GeneratedRegex("openTemplateDialog\\(\\s*['\"](?<url>[^'\"]+)['\"]", RegexOptions.IgnoreCase)]
    private static partial Regex TemplateUrlRegex();
}
