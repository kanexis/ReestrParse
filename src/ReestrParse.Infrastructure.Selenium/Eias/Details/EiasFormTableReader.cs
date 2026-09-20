using System.Text.RegularExpressions;
using AngleSharp.Dom;

namespace ReestrParse.Infrastructure.Selenium.Eias.Details;

/// <summary>
/// Общие операции над HTML-таблицами, которые TemplatePrinter генерирует из Excel-листов.
/// Код параметра используется как стабильный ключ; физический номер HTML-строки не используется.
/// </summary>
internal static partial class EiasFormTableReader
{
    public static IElement? FindTable(
        IDocument document,
        params string[] requiredTitleFragments)
    {
        foreach (var cell in document.QuerySelectorAll("td"))
        {
            var text = Normalize(cell.TextContent);
            if (requiredTitleFragments.Any(fragment =>
                    !text.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
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

    public static Dictionary<string, List<string>> ReadParameters(IElement table)
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

    public static string First(
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

    public static IReadOnlyList<string> All(
        IReadOnlyDictionary<string, List<string>> values,
        string code)
        => values.TryGetValue(code, out var items)
            ? items.Where(IsMeaningful)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : [];

    public static bool IsMeaningful(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var normalized = Normalize(value);
        return normalized.Length > 0 &&
               !PlaceholderValues.Contains(normalized);
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(" ", value
            .Replace('\u00A0', ' ')
            .Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static readonly HashSet<string> PlaceholderValues = new(
        ["x", "х", "нет", "отсутствует", "не указано", "не имеется", "н/д", "-", "—"],
        StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"^\d+(?:\.\d+)*$")]
    private static partial Regex ParameterCodeRegex();
}
