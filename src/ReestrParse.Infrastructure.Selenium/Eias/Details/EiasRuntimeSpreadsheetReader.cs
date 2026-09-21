using System.Text.Json;
using System.Text.RegularExpressions;
using OpenQA.Selenium;

namespace ReestrParse.Infrastructure.Selenium.Eias.Details;

/// <summary>
/// TemplatePrinter часть форм рисует через GrapeCity/Wijmo Spread в Canvas.
/// Видимые значения при этом отсутствуют в PageSource. Поэтому snapshot читается
/// из живой JS-модели spreadsheet (getText/getValue/getFormula).
/// </summary>
internal static partial class EiasRuntimeSpreadsheetReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyDictionary<string, List<string>> TryReadParameters(IWebDriver driver)
    {
        try
        {
            driver.SwitchTo().DefaultContent();
            var json = ((IJavaScriptExecutor)driver).ExecuteScript(RuntimeSnapshotScript)?.ToString();
            if (string.IsNullOrWhiteSpace(json) || json == "[]")
                return Empty();

            var rows = JsonSerializer.Deserialize<List<SpreadRow>>(json, JsonOptions) ?? [];
            if (rows.Count == 0)
                return Empty();

            var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            foreach (var row in rows)
            {
                var ordered = row.Cells.OrderBy(x => x.Column).ToArray();
                for (var i = 0; i < ordered.Length; i++)
                {
                    var code = Normalize(FirstNonEmpty(ordered[i].Text, ordered[i].Value));
                    if (!ParameterCodeRegex().IsMatch(code))
                        continue;

                    // В формах справа от кода обычно идут: наименование параметра,
                    // значение, затем (иногда) пояснение. Последнее значение брать нельзя:
                    // в современной форме 1 так можно случайно забрать текст инструкции.
                    // Сначала предпочитаем вычисляемую/formula-ячейку; иначе берём второе
                    // содержательное значение справа (label -> value), а если оно одно — его.
                    var right = ordered.Skip(i + 1).ToArray();
                    var formulaValue = right
                        .Where(c => !string.IsNullOrWhiteSpace(c.Formula))
                        .Select(c => Normalize(FirstNonEmpty(c.Text, c.Value)))
                        .FirstOrDefault(IsRuntimeValue);

                    var meaningful = right
                        .Select(c => Normalize(FirstNonEmpty(c.Text, c.Value)))
                        .Where(IsRuntimeValue)
                        .ToArray();

                    var value = !string.IsNullOrWhiteSpace(formulaValue)
                        ? formulaValue
                        : meaningful.Length >= 2
                            ? meaningful[1]
                            : meaningful.FirstOrDefault() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    if (!result.TryGetValue(code, out var bucket))
                    {
                        bucket = [];
                        result[code] = bucket;
                    }

                    if (!bucket.Contains(value, StringComparer.OrdinalIgnoreCase))
                        bucket.Add(value);
                }
            }

            return result;
        }
        catch (WebDriverException)
        {
            return Empty();
        }
        catch (JsonException)
        {
            return Empty();
        }
    }

    public static bool IsSpreadReady(IWebDriver driver)
    {
        try
        {
            driver.SwitchTo().DefaultContent();
            return Convert.ToBoolean(((IJavaScriptExecutor)driver).ExecuteScript("""
                const host = document.querySelector(".spreadHost[gcuielement='gcSpread'], [gcuielement='gcSpread']");
                const canvas = document.querySelector("canvas[gcuielement='gcWorksheetCanvas']");
                if (!host || !canvas || canvas.width <= 0 || canvas.height <= 0) return false;

                const preloader = document.getElementById('preloader');
                if (preloader) {
                    const s = getComputedStyle(preloader);
                    const visible = s.display !== 'none' && s.visibility !== 'hidden' && s.opacity !== '0';
                    if (visible) return false;
                }

                return true;
                """));
        }
        catch (WebDriverException)
        {
            return false;
        }
    }

    private static IReadOnlyDictionary<string, List<string>> Empty()
        => new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

    private static string Normalize(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : string.Join(" ", value.Replace('\u00A0', ' ').Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static bool IsRuntimeValue(string value)
    {
        if (!EiasFormTableReader.IsMeaningful(value))
            return false;

        if (ParameterCodeRegex().IsMatch(value))
            return false;

        if (value.StartsWith("=", StringComparison.Ordinal))
            return false;

        return true;
    }

    private const string RuntimeSnapshotScript = """
        const host = document.querySelector(".spreadHost[gcuielement='gcSpread'], [gcuielement='gcSpread']");
        if (!host) return '[]';

        let spread = null;

        try {
            if (window.GC?.Spread?.Sheets?.findControl) {
                spread = window.GC.Spread.Sheets.findControl(host);
            }
        } catch (_) {}

        const jq = window.jQuery;
        if (!spread && jq) {
            const $host = jq(host);
            const dataKeys = ['wijmo-wijspread', 'wijspread', 'gcSpread', 'spread'];
            for (const key of dataKeys) {
                try {
                    const item = $host.data(key);
                    if (item) {
                        spread = item.spread || item;
                        if (spread) break;
                    }
                } catch (_) {}
            }

            if (!spread) {
                try { spread = $host.wijspread('spread'); } catch (_) {}
            }
        }

        if (!spread)
            spread = host.spread || host._spread || host.gcSpread || null;
        if (!spread) return '[]';

        const getSheetCount = () => {
            try { if (typeof spread.getSheetCount === 'function') return spread.getSheetCount(); } catch (_) {}
            try { if (Array.isArray(spread.sheets)) return spread.sheets.length; } catch (_) {}
            return 1;
        };

        const getSheet = index => {
            try { if (typeof spread.getSheet === 'function') return spread.getSheet(index); } catch (_) {}
            try { if (Array.isArray(spread.sheets)) return spread.sheets[index]; } catch (_) {}
            try { if (index === 0 && typeof spread.getActiveSheet === 'function') return spread.getActiveSheet(); } catch (_) {}
            return null;
        };

        const read = (sheet, method, row, col) => {
            try {
                if (sheet && typeof sheet[method] === 'function') {
                    const value = sheet[method](row, col);
                    return value == null ? '' : String(value);
                }
            } catch (_) {}
            return '';
        };

        const rowCount = sheet => {
            try { if (typeof sheet.getRowCount === 'function') return Math.min(sheet.getRowCount(), 1200); } catch (_) {}
            return Math.min(Number(sheet.rowCount || 0), 1200);
        };

        const colCount = sheet => {
            try { if (typeof sheet.getColumnCount === 'function') return Math.min(sheet.getColumnCount(), 120); } catch (_) {}
            return Math.min(Number(sheet.columnCount || 0), 120);
        };

        const sheetName = (sheet, index) => {
            try { if (typeof sheet.getName === 'function') return String(sheet.getName() || `sheet-${index}`); } catch (_) {}
            try { if (sheet.name) return String(sheet.name); } catch (_) {}
            return `sheet-${index}`;
        };

        const result = [];
        const count = Math.min(getSheetCount(), 20);
        for (let s = 0; s < count; s++) {
            const sheet = getSheet(s);
            if (!sheet) continue;
            const rows = rowCount(sheet);
            const cols = colCount(sheet);
            const name = sheetName(sheet, s);

            for (let r = 0; r < rows; r++) {
                const cells = [];
                for (let c = 0; c < cols; c++) {
                    const text = read(sheet, 'getText', r, c);
                    const value = read(sheet, 'getValue', r, c);
                    const formula = read(sheet, 'getFormula', r, c);
                    if (!text && !value && !formula) continue;
                    cells.push({ column: c, text, value, formula });
                }
                if (cells.length) result.push({ sheet: name, row: r, cells });
            }
        }

        return JSON.stringify(result);
        """;

    [GeneratedRegex(@"^\d+(?:\.\d+)*$")]
    private static partial Regex ParameterCodeRegex();

    private sealed record SpreadRow(string Sheet, int Row, IReadOnlyList<SpreadCell> Cells);
    private sealed record SpreadCell(int Column, string Text, string Value, string Formula);
}
