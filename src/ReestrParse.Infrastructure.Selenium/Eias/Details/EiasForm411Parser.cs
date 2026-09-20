using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ReestrParse.Domain.Organizations;

namespace ReestrParse.Infrastructure.Selenium.Eias.Details;

/// <summary>
/// Парсер HTML-представления workbook из TemplatePrinter. Лист 4.1.1 определяется
/// по заголовку формы; клики по вкладкам листов не требуются, потому что данные уже
/// присутствуют в DOM.
/// </summary>
internal static partial class EiasForm411Parser
{
    public static OrganizationContactDetails Parse(
        string html,
        OrganizationReference source,
        string detailUrl,
        string templateUrl)
    {
        if (string.IsNullOrWhiteSpace(html))
            throw new InvalidOperationException("TemplatePrinter вернул пустой HTML.");

        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        var formTable = FindForm411Table(document)
            ?? throw new InvalidOperationException("В TemplatePrinter не найден лист «Форма 4.1.1».");

        var values = ReadParameters(formTable);

        var name = First(values, "2.1", source.Name);
        var inn = First(values, "2.2", source.Inn);
        var kpp = First(values, "2.3", source.Kpp);

        var phones = All(values, "7.1")
            .Where(IsMeaningful)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new OrganizationContactDetails(
            OrganizationId: source.OrganizationId,
            Name: name,
            Inn: inn,
            Kpp: kpp,
            Phones: phones,
            Email: First(values, "9"),
            Website: First(values, "8"),
            ResponsibleFullName: JoinName(
                First(values, "3.1.1"),
                First(values, "3.1.2"),
                First(values, "3.1.3")),
            ResponsiblePosition: First(values, "3.2"),
            ResponsiblePhone: First(values, "3.3"),
            ResponsibleEmail: First(values, "3.4"),
            ManagerFullName: JoinName(
                First(values, "4.1"),
                First(values, "4.2"),
                First(values, "4.3")),
            PostalAddress: First(values, "5"),
            LocationAddress: First(values, "6"),
            DetailUrl: detailUrl,
            TemplateUrl: templateUrl);
    }

    private static IElement? FindForm411Table(IDocument document)
    {
        foreach (var cell in document.QuerySelectorAll("td"))
        {
            var text = Normalize(cell.TextContent);
            if (!text.Contains("Форма 4.1.1", StringComparison.OrdinalIgnoreCase) ||
                !text.Contains("Общая информация об организации", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            IElement? current = cell;
            while (current is not null)
            {
                if (string.Equals(current.LocalName, "table", StringComparison.OrdinalIgnoreCase))
                    return current;

                current = current.ParentElement;
            }
        }

        return null;
    }

    private static Dictionary<string, List<string>> ReadParameters(IElement table)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in table.QuerySelectorAll("tr"))
        {
            var cells = row.Children
                .Where(x => string.Equals(x.LocalName, "td", StringComparison.OrdinalIgnoreCase))
                .Where(x => !x.ClassList.Contains("row-number"))
                .ToArray();

            if (cells.Length < 3)
                continue;

            var code = Normalize(cells[0].TextContent);
            if (!ParameterCodeRegex().IsMatch(code))
                continue;

            var value = Normalize(cells[2].TextContent);
            if (!result.TryGetValue(code, out var bucket))
            {
                bucket = [];
                result[code] = bucket;
            }

            if (!string.IsNullOrWhiteSpace(value))
                bucket.Add(value);
        }

        return result;
    }

    private static string First(
        IReadOnlyDictionary<string, List<string>> values,
        string code,
        string fallback = "")
    {
        if (values.TryGetValue(code, out var items))
        {
            var value = items.FirstOrDefault(IsMeaningful);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return fallback;
    }

    private static IReadOnlyList<string> All(
        IReadOnlyDictionary<string, List<string>> values,
        string code)
        => values.TryGetValue(code, out var items) ? items : [];

    private static bool IsMeaningful(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = Normalize(value);
        return normalized.Length > 0 &&
               !string.Equals(normalized, "x", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(normalized, "отсутствует", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(normalized, "не указано", StringComparison.OrdinalIgnoreCase);
    }

    private static string JoinName(params string[] parts)
        => string.Join(" ", parts.Where(IsMeaningful));

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value
            .Replace('\u00A0', ' ')
            .Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    [GeneratedRegex(@"^\d+(?:\.\d+)*$")]
    private static partial Regex ParameterCodeRegex();
}
