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
            var inferredValueColumns = InferValueColumns(rows);

            foreach (var row in rows)
            {
                var ordered = row.Cells.OrderBy(x => x.Column).ToArray();
                var codeIndex = Array.FindIndex(ordered, x =>
                    ParameterCodeRegex().IsMatch(Normalize(FirstNonEmpty(x.Text, x.Value))));

                if (codeIndex < 0)
                    continue;

                var code = Normalize(FirstNonEmpty(ordered[codeIndex].Text, ordered[codeIndex].Value));
                var right = ordered.Skip(codeIndex + 1).ToArray();
                var formulaCells = right
                    .Where(c => !string.IsNullOrWhiteSpace(c.Formula))
                    .ToArray();

                int? inferredColumn = inferredValueColumns.TryGetValue(row.Sheet, out var column)
                    ? column
                    : null;

                string value;

                if (formulaCells.Length > 0)
                {
                    // В современных Canvas/Spread формах value-cell часто formula-backed.
                    // Если формула существует, но её результат пустой, параметр реально
                    // не заполнен. НЕЛЬЗЯ искать следующую непустую ячейку справа: там
                    // может быть подпись/инструкция другого блока, что и давало «съезд».
                    var formulaCell = inferredColumn is int
                        ? formulaCells
                            .OrderBy(c => Math.Abs(c.Column - column))
                            .FirstOrDefault(c => Math.Abs(c.Column - column) <= 2)
                        : formulaCells[0];

                    if (formulaCell is null)
                        continue;

                    value = Normalize(FirstNonEmpty(formulaCell.Text, formulaCell.Value));
                    if (!IsRuntimeValue(value))
                        continue;
                }
                else
                {
                    value = ReadStaticValue(right, inferredColumn);
                    if (!IsRuntimeValue(value))
                        continue;
                }

                var label = right
                    .Where(c => string.IsNullOrWhiteSpace(c.Formula))
                    .Select(c => Normalize(FirstNonEmpty(c.Text, c.Value)))
                    .FirstOrDefault(IsRuntimeValue) ?? string.Empty;

                EiasFormTableReader.AddParameter(result, code, label, value);
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

    private static IReadOnlyDictionary<string, int> InferValueColumns(IReadOnlyList<SpreadRow> rows)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var sheetGroup in rows.GroupBy(x => x.Sheet, StringComparer.OrdinalIgnoreCase))
        {
            // Formula-backed cells — самый сильный якорь реальной value-column.
            // Берём наиболее частую колонку в листе, а не позицию конкретной строки.
            var formulaColumns = sheetGroup
                .SelectMany(row => row.Cells)
                .Where(cell => !string.IsNullOrWhiteSpace(cell.Formula))
                .GroupBy(cell => cell.Column)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key)
                .ToArray();

            if (formulaColumns.Length > 0)
            {
                result[sheetGroup.Key] = formulaColumns[0].Key;
                continue;
            }

            // Для static Spread без формул определяем value-column статистически.
            // Первое содержательное поле справа от кода — label, второе — value.
            // Мода по всему листу переживает отдельные пропущенные значения.
            var candidates = new List<int>();
            foreach (var row in sheetGroup)
            {
                var ordered = row.Cells.OrderBy(x => x.Column).ToArray();
                var codeIndex = Array.FindIndex(ordered, x =>
                    ParameterCodeRegex().IsMatch(Normalize(FirstNonEmpty(x.Text, x.Value))));
                if (codeIndex < 0)
                    continue;

                var meaningful = ordered
                    .Skip(codeIndex + 1)
                    .Where(x => IsRuntimeValue(Normalize(FirstNonEmpty(x.Text, x.Value))))
                    .Take(2)
                    .ToArray();

                if (meaningful.Length == 2)
                    candidates.Add(meaningful[1].Column);
            }

            if (candidates.Count > 0)
            {
                result[sheetGroup.Key] = candidates
                    .GroupBy(x => x)
                    .OrderByDescending(x => x.Count())
                    .ThenBy(x => x.Key)
                    .First()
                    .Key;
            }
        }

        return result;
    }

    private static string ReadStaticValue(IReadOnlyList<SpreadCell> right, int? preferredColumn)
    {
        if (preferredColumn is int column)
        {
            // Snapshot теперь сохраняет пустые cells. Если value-column пуст —
            // возвращаем пусто, а не перепрыгиваем к следующему тексту справа.
            var exact = right.FirstOrDefault(c => c.Column == column);
            if (exact is not null)
                return Normalize(FirstNonEmpty(exact.Text, exact.Value));

            var near = right
                .Where(c => Math.Abs(c.Column - column) <= 1)
                .OrderBy(c => Math.Abs(c.Column - column))
                .FirstOrDefault();
            if (near is not null)
                return Normalize(FirstNonEmpty(near.Text, near.Value));
        }

        // Очень старый workbook без формул/стабильной value-column. Последний fallback
        // допускается лишь при небольшом физическом расстоянии между label и value.
        var meaningful = right
            .Where(c => IsRuntimeValue(Normalize(FirstNonEmpty(c.Text, c.Value))))
            .Take(2)
            .ToArray();

        if (meaningful.Length < 2)
            return string.Empty;

        if (meaningful[1].Column - meaningful[0].Column > 12)
            return string.Empty;

        return Normalize(FirstNonEmpty(meaningful[1].Text, meaningful[1].Value));
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

        const normalize = value => String(value || '')
            .replace(/\u00a0/g, ' ')
            .replace(/\s+/g, ' ')
            .trim();
        const parameterCode = /^\d+(?:\.\d+)*$/;

        const result = [];
        const count = Math.min(getSheetCount(), 20);
        for (let s = 0; s < count; s++) {
            const sheet = getSheet(s);
            if (!sheet) continue;
            const rows = rowCount(sheet);
            const cols = colCount(sheet);
            const name = sheetName(sheet, s);

            for (let r = 0; r < rows; r++) {
                let codeColumn = -1;

                // Сначала находим строку параметра. Затем возвращаем окно cells
                // ВМЕСТЕ с пустыми ячейками. Так C# видит, что value-cell пуст,
                // и не принимает следующий label за значение текущего параметра.
                for (let c = 0; c < cols; c++) {
                    const text = read(sheet, 'getText', r, c);
                    const value = read(sheet, 'getValue', r, c);
                    if (parameterCode.test(normalize(text || value))) {
                        codeColumn = c;
                        break;
                    }
                }

                if (codeColumn < 0) continue;

                const cells = [];
                const endColumn = Math.min(cols, codeColumn + 36);
                for (let c = codeColumn; c < endColumn; c++) {
                    cells.push({
                        column: c,
                        text: read(sheet, 'getText', r, c),
                        value: read(sheet, 'getValue', r, c),
                        formula: read(sheet, 'getFormula', r, c)
                    });
                }

                result.push({ sheet: name, row: r, cells });
            }
        }

        return JSON.stringify(result);
        """;

    [GeneratedRegex(@"^\d+(?:\.\d+)*$")]
    private static partial Regex ParameterCodeRegex();

    private sealed record SpreadRow(string Sheet, int Row, IReadOnlyList<SpreadCell> Cells);
    private sealed record SpreadCell(int Column, string Text, string Value, string Formula);
}
