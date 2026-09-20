using OpenQA.Selenium;

namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Читает состояние только актуального отрисованного pager ASPxGridView2.
/// DevExpress может оставлять старые/скрытые узлы после callback/postback, поэтому
/// нельзя брать первый .dxp-summary из PageSource.
/// </summary>
internal static class EiasLivePager
{
    public static EiasPagerState Read(IWebDriver driver)
    {
        SafeDefaultContent(driver);

        try
        {
            var raw = ((IJavaScriptExecutor)driver).ExecuteScript("""
                const rendered = el => {
                    if (!el) return false;
                    const s = getComputedStyle(el);
                    return s.display !== 'none' &&
                           s.visibility !== 'hidden' &&
                           s.opacity !== '0' &&
                           el.getClientRects().length > 0;
                };

                const pagers = Array.from(document.querySelectorAll(
                    "[id='ASPxGridView2_DXPagerBottom']"
                ));

                const pager = pagers.find(rendered) || pagers[pagers.length - 1];
                if (!pager)
                    return '1\t1\t0\t';

                const summary = (pager.querySelector('.dxp-summary')?.textContent || '')
                    .replace(/\s+/g, ' ')
                    .trim();

                const currentText = (pager.querySelector('.dxp-current')?.textContent || '')
                    .replace(/\D/g, '');

                let currentPage = currentText ? Number(currentText) : 1;
                let totalPages = 1;
                let totalRows = 0;

                const pageMatch = summary.match(/Страница\s+(\d+)\s+(?:of|из)\s+(\d+)/i);
                if (pageMatch) {
                    currentPage = Number(pageMatch[1]) || currentPage;
                    totalPages = Number(pageMatch[2]) || 1;
                } else {
                    const numbers = Array.from(pager.querySelectorAll('.dxp-num'))
                        .map(x => Number((x.textContent || '').replace(/\D/g, '')))
                        .filter(Number.isFinite)
                        .filter(x => x > 0);

                    if (numbers.length > 0)
                        totalPages = Math.max(currentPage, ...numbers);
                }

                const rowsMatch = summary.match(/Всего\s+строк:\s*(\d+)/i);
                if (rowsMatch)
                    totalRows = Number(rowsMatch[1]) || 0;

                return `${currentPage}\t${totalPages}\t${totalRows}\t${summary}`;
                """)?.ToString() ?? string.Empty;

            var parts = raw.Split('\t', 4);

            var currentPage = parts.Length > 0 && int.TryParse(parts[0], out var current)
                ? current
                : 1;

            var totalPages = parts.Length > 1 && int.TryParse(parts[1], out var total)
                ? total
                : 1;

            var totalRows = parts.Length > 2 && int.TryParse(parts[2], out var rows)
                ? rows
                : 0;

            var summary = parts.Length > 3 ? parts[3] : string.Empty;

            return new EiasPagerState(
                Math.Max(1, currentPage),
                Math.Max(1, totalPages),
                Math.Max(0, totalRows),
                summary);
        }
        catch (WebDriverException)
        {
            return new EiasPagerState(1, 1, 0, string.Empty);
        }
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
}

internal sealed record EiasPagerState(
    int CurrentPage,
    int TotalPages,
    int TotalRows,
    string Summary);
