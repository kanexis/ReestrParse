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

            if (cells.Length < 2)
                continue;

            // Код параметра ищем внутри строки, а не считаем, что он всегда cells[0].
            // Разные TemplatePrinter могут добавлять служебные/пустые TD перед кодом.
            var codeIndex = Array.FindIndex(cells, cell =>
                ParameterCodeRegex().IsMatch(Normalize(cell.TextContent)));
            if (codeIndex < 0)
                continue;

            var code = Normalize(cells[codeIndex].TextContent);

            // После кода идёт label, затем value-cell. Если value-cell пустая —
            // значение остаётся пустым; мы НЕ ищем следующую непустую TD.
            var valueIndex = codeIndex + 2;
            var value = valueIndex < cells.Length
                ? Normalize(cells[valueIndex].TextContent)
                : string.Empty;
            var label = codeIndex + 1 < cells.Length
                ? Normalize(cells[codeIndex + 1].TextContent)
                : string.Empty;

            AddParameter(result, code, label, value);
        }

        return result;
    }

    internal const string LabelKeyPrefix = "@label:";

    internal static void AddParameter(
        IDictionary<string, List<string>> result,
        string code,
        string? label,
        string? value)
    {
        var normalizedValue = Normalize(value);
        if (!IsMeaningful(normalizedValue))
            return;

        AddValue(result, code, normalizedValue);

        var normalizedLabel = Normalize(label);
        if (IsMeaningful(normalizedLabel))
            AddValue(result, LabelKeyPrefix + normalizedLabel.ToLowerInvariant(), normalizedValue);
    }

    private static void AddValue(
        IDictionary<string, List<string>> result,
        string key,
        string value)
    {
        if (!result.TryGetValue(key, out var bucket))
        {
            bucket = [];
            result[key] = bucket;
        }

        if (!bucket.Contains(value, StringComparer.OrdinalIgnoreCase))
            bucket.Add(value);
    }

    public static string FirstByCodeOrLabel(
        IReadOnlyDictionary<string, List<string>> values,
        string code,
        params string[] labelFragments)
    {
        var byCode = First(values, code);
        if (IsMeaningful(byCode))
            return byCode;

        return FirstByLabel(values, labelFragments);
    }

    public static IReadOnlyList<string> AllByCodeOrLabel(
        IReadOnlyDictionary<string, List<string>> values,
        string code,
        params string[] labelFragments)
    {
        // У некоторых версий формы одно логическое поле разложено на дочерние
        // параметры (например, 9, 9.1, 9.2 для телефонов). Собираем и корень,
        // и дочерние коды, но никогда не смешиваем их с синтетическими @label:.
        var result = values
            .Where(pair =>
                !pair.Key.StartsWith(LabelKeyPrefix, StringComparison.OrdinalIgnoreCase) &&
                (string.Equals(pair.Key, code, StringComparison.OrdinalIgnoreCase) ||
                 pair.Key.StartsWith(code + ".", StringComparison.OrdinalIgnoreCase)))
            .SelectMany(pair => pair.Value)
            .Where(IsMeaningful)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var item in ValuesByLabel(values, labelFragments))
        {
            if (!result.Contains(item, StringComparer.OrdinalIgnoreCase))
                result.Add(item);
        }

        return result;
    }

    public static string FirstByLabel(
        IReadOnlyDictionary<string, List<string>> values,
        params string[] labelFragments)
        => ValuesByLabel(values, labelFragments).FirstOrDefault() ?? string.Empty;

    private static IEnumerable<string> ValuesByLabel(
        IReadOnlyDictionary<string, List<string>> values,
        IReadOnlyList<string> labelFragments)
    {
        var normalizedFragments = labelFragments
            .Select(Normalize)
            .Where(x => x.Length > 0)
            .ToArray();

        if (normalizedFragments.Length == 0)
            yield break;

        foreach (var pair in values)
        {
            if (!pair.Key.StartsWith(LabelKeyPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var label = pair.Key[LabelKeyPrefix.Length..];
            if (!normalizedFragments.Any(fragment =>
                    label.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            foreach (var value in pair.Value.Where(IsMeaningful))
                yield return value;
        }
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
