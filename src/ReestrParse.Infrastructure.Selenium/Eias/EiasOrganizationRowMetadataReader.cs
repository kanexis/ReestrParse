using System.Text.Json;
using System.Text.RegularExpressions;
using OpenQA.Selenium;
using ReestrParse.Domain.Catalog;

namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Снимает ключи строк текущей страницы ASPxGridView2 прямо из live DOM.
/// Если DevExpress отдаёт внутренний row key, он используется как orgId и позволяет
/// сформировать прямую ссылку на карточку организации без клика/Back.
/// </summary>
internal static partial class EiasOrganizationRowMetadataReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static IReadOnlyDictionary<int, OrganizationRowMetadata> Read(
        IWebDriver driver,
        RegionOption region,
        SphereOption sphere)
    {
        SafeDefaultContent(driver);

        try
        {
            var json = ((IJavaScriptExecutor)driver).ExecuteScript("""
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
                if (!table) return '[]';

                let grid = window.ASPxGridView2 || null;
                try {
                    if (!grid && window.ASPx && typeof ASPx.GetControlCollection === 'function') {
                        grid = ASPx.GetControlCollection().GetByName('ASPxGridView2');
                    }
                } catch (_) {}

                const start = Number(grid?.visibleStartIndex || 0);
                const rows = Array.from(table.querySelectorAll(
                    "tr[id^='ASPxGridView2_DXDataRow']"
                ));

                const result = rows.map((row, fallbackIndex) => {
                    const match = (row.id || '').match(/DXDataRow(\d+)$/i);
                    const localIndex = match ? Number(match[1]) : fallbackIndex;
                    const visibleIndex = start + localIndex;

                    let key = '';
                    try {
                        if (grid && typeof grid.GetRowKey === 'function') {
                            const value = grid.GetRowKey(visibleIndex);
                            if (value !== null && value !== undefined)
                                key = String(value);
                        }
                    } catch (_) {}

                    if (!key && grid) {
                        const keyArrays = [
                            grid.keys,
                            grid.stateObject?.keys,
                            grid.stateObject?.Keys,
                            grid.cpKeys
                        ];

                        for (const keys of keyArrays) {
                            if (!Array.isArray(keys)) continue;
                            const value = keys[visibleIndex] ?? keys[localIndex];
                            if (value !== null && value !== undefined && String(value).trim()) {
                                key = String(value);
                                break;
                            }
                        }
                    }

                    if (!key) {
                        const attrKey = row.getAttribute('data-key')
                            || row.getAttribute('data-orgid')
                            || row.getAttribute('data-org-id');
                        if (attrKey) key = attrKey;
                    }

                    let detailUrl = '';
                    const candidates = [row, ...row.querySelectorAll('a')];
                    for (const el of candidates) {
                        const attributes = Array.from(el.attributes || [])
                            .map(a => a.value || '')
                            .join(' ');
                        const raw = `${el.getAttribute('href') || ''} ${el.getAttribute('onclick') || ''} ${attributes}`;
                        const urlMatch = raw.match(/(?:https?:\/\/[^'\"\s]+)?\/?Discl\/PublicDisclosureInfoOrg\.aspx\?[^'\"\s<>]+/i);
                        if (urlMatch) {
                            detailUrl = urlMatch[0].replace(/&amp;/gi, '&');
                            break;
                        }
                    }

                    return { localIndex, key, detailUrl };
                });

                return JSON.stringify(result);
                """)?.ToString();

            List<RawRowMetadata> rows = string.IsNullOrWhiteSpace(json)
                ? new List<RawRowMetadata>()
                : JsonSerializer.Deserialize<List<RawRowMetadata>>(json, JsonOptions)
                    ?? new List<RawRowMetadata>();

            var formValue = ReadSelectedFormValue(driver);
            var baseUri = Uri.TryCreate(driver.Url, UriKind.Absolute, out var currentUri)
                ? currentUri
                : null;

            var result = new Dictionary<int, OrganizationRowMetadata>();

            foreach (var row in rows)
            {
                var organizationId = ExtractOrganizationId(row.Key, row.DetailUrl);
                var detailUrl = NormalizeDetailUrl(row.DetailUrl, baseUri);

                if (string.IsNullOrWhiteSpace(detailUrl) && !string.IsNullOrWhiteSpace(organizationId))
                {
                    detailUrl = EiasOrganizationUrlBuilder.Build(
                        region,
                        sphere,
                        formValue,
                        organizationId);
                }

                result[row.LocalIndex] = new OrganizationRowMetadata(
                    row.LocalIndex,
                    organizationId,
                    detailUrl);
            }

            return result;
        }
        catch (WebDriverException)
        {
            return new Dictionary<int, OrganizationRowMetadata>();
        }
    }

    public static string ReadSelectedFormValue(IWebDriver driver)
    {
        try
        {
            return ((IJavaScriptExecutor)driver).ExecuteScript("""
                const hidden = document.getElementById('FormSelectValue');
                if (hidden?.value) return hidden.value;

                const checkbox = document.getElementById('ui-multiselect-FormSelect-option-10')
                    || document.querySelector("input[name='multiselect_FormSelect'][value*='F_W_O_4_1_1']");
                return checkbox?.value || '';
                """)?.ToString() ?? string.Empty;
        }
        catch (WebDriverException)
        {
            return string.Empty;
        }
    }

    private static string ExtractOrganizationId(string? key, string? detailUrl)
    {
        if (!string.IsNullOrWhiteSpace(detailUrl))
        {
            var match = OrgIdRegex().Match(detailUrl);
            if (match.Success)
                return match.Groups[1].Value;
        }

        if (string.IsNullOrWhiteSpace(key))
            return string.Empty;

        var trimmed = key.Trim();
        if (trimmed.All(char.IsDigit) && trimmed.Length <= 9)
            return trimmed;

        var keyMatch = ShortNumericKeyRegex().Match(trimmed);
        return keyMatch.Success ? keyMatch.Groups[1].Value : string.Empty;
    }

    private static string? NormalizeDetailUrl(string? raw, Uri? baseUri)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var value = raw.Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase).Trim();
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        return baseUri is not null && Uri.TryCreate(baseUri, value, out var relative)
            ? relative.ToString()
            : null;
    }

    private static void SafeDefaultContent(IWebDriver driver)
    {
        try { driver.SwitchTo().DefaultContent(); }
        catch (NoSuchFrameException) { }
        catch (StaleElementReferenceException) { }
    }

    [GeneratedRegex(@"[?&]orgId=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex OrgIdRegex();

    [GeneratedRegex(@"(?:^|\D)(\d{5,9})(?:\D|$)")]
    private static partial Regex ShortNumericKeyRegex();

    private sealed record RawRowMetadata(int LocalIndex, string Key, string DetailUrl);
}

internal sealed record OrganizationRowMetadata(
    int LocalIndex,
    string OrganizationId,
    string? DetailUrl);
