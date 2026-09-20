using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Отдельный этап поиска. Сначала страница фильтров должна полностью загрузиться,
/// затем отдельным вызовом нажимается только #searchBtn, и только после этого
/// ожидаются строки ASPxGridView2.
/// </summary>
internal static class EiasSearchExecutor
{
    public static void WaitUntilReady(IWebDriver driver, WebDriverWait wait)
    {
        EiasPageWaiter.WaitForDocumentReady(driver, wait);
        EiasPageWaiter.WaitForSearchPageReady(driver, wait);
    }

    public static void ClickSearch(IWebDriver driver, WebDriverWait wait)
    {
        WaitUntilReady(driver, wait);

        var clicked = EiasPageWaiter.ExecuteBool(driver, """
            const button = document.getElementById('searchBtn');
            if (!button) return false;
            button.click();
            return true;
            """);

        if (!clicked)
            throw new NoSuchElementException("Кнопка НАЙТИ (#searchBtn) не найдена на полностью загруженной странице.");
    }

    public static void WaitForFirstPage(IWebDriver driver, CancellationToken cancellationToken)
    {
        var resultWait = new WebDriverWait(driver, TimeSpan.FromSeconds(60))
        {
            PollingInterval = TimeSpan.FromMilliseconds(300)
        };

        try
        {
            resultWait.Until(d =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                return EiasPageWaiter.ExecuteBool(d, """
                    if (document.readyState !== 'complete') return false;

                    const rows = document.querySelectorAll(
                        "#ASPxGridView2_DXMainTable tr[id^='ASPxGridView2_DXDataRow']"
                    );

                    if (rows.length === 0) return false;

                    const lp = document.getElementById('ASPxGridView2_LP');
                    const ld = document.getElementById('ASPxGridView2_LD');
                    const visible = el => {
                        if (!el) return false;
                        const s = getComputedStyle(el);
                        return s.display !== 'none' && s.visibility !== 'hidden';
                    };

                    return !visible(lp) && !visible(ld);
                    """);
            });
        }
        catch (WebDriverTimeoutException ex)
        {
            var diagnostic = SaveDiagnosticPage(driver);
            throw new WebDriverTimeoutException(
                "Кнопка #searchBtn была нажата отдельно после полной загрузки страницы, " +
                "но строки ASPxGridView2_DXDataRow* не появились. " +
                $"HTML после поиска сохранён: {diagnostic}", ex);
        }
    }

    private static string SaveDiagnosticPage(IWebDriver driver)
    {
        try
        {
            driver.SwitchTo().DefaultContent();
            var directory = Path.Combine(AppContext.BaseDirectory, "diagnostics");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, $"eias-after-search-{DateTime.Now:yyyyMMdd-HHmmss}.html");
            File.WriteAllText(path, driver.PageSource);
            return path;
        }
        catch (Exception ex)
        {
            return $"не удалось сохранить ({ex.Message})";
        }
    }
}
