using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Нажимает конкретную кнопку поиска ЕИАС (#searchBtn) и ждёт,
/// пока после postback появится первая строка ASPxGridView2.
/// Никакого эвристического поиска кнопок по тексту.
/// </summary>
internal static class EiasFilterSubmitter
{
    public static void ClickFindAndWaitForOrganizations(IWebDriver driver, WebDriverWait wait)
    {
        driver.SwitchTo().DefaultContent();

        // Ждём именно известную кнопку:
        // <a id="searchBtn" href="javascript:__doPostBack('searchBtn','')">НАЙТИ</a>
        wait.Until(d =>
        {
            try
            {
                d.SwitchTo().DefaultContent();
                return Convert.ToBoolean(((IJavaScriptExecutor)d).ExecuteScript(
                    "return !!document.getElementById(arguments[0]);",
                    EiasDomHints.SearchButtonId));
            }
            catch (WebDriverException)
            {
                return false;
            }
        });

        var clicked = Convert.ToBoolean(((IJavaScriptExecutor)driver).ExecuteScript("""
            const button = document.getElementById(arguments[0]);
            if (!button) return false;

            // Нажимаем сам anchor. Его href вызывает __doPostBack('searchBtn','').
            button.click();
            return true;
            """, EiasDomHints.SearchButtonId));

        if (!clicked)
            throw new NoSuchElementException("Кнопка НАЙТИ (#searchBtn) не найдена.");

        // Во время полного postback старый document может исчезнуть, поэтому
        // любые WebDriverException здесь считаем нормальным состоянием ожидания.
        Thread.Sleep(250);

        try
        {
            wait.Until(d =>
            {
                try
                {
                    d.SwitchTo().DefaultContent();

                    var ready = ((IJavaScriptExecutor)d)
                        .ExecuteScript("return document.readyState")
                        ?.ToString();

                    if (!string.Equals(ready, "complete", StringComparison.OrdinalIgnoreCase))
                        return false;

                    return Convert.ToBoolean(((IJavaScriptExecutor)d).ExecuteScript("""
                        const main = document.getElementById('ASPxGridView2_DXMainTable');
                        if (!main) return false;

                        const rows = main.querySelectorAll(
                            "tr[id^='ASPxGridView2_DXDataRow']"
                        );

                        const lp = document.getElementById('ASPxGridView2_LP');
                        const ld = document.getElementById('ASPxGridView2_LD');

                        const visible = el => {
                            if (!el) return false;
                            const s = getComputedStyle(el);
                            return s.display !== 'none' &&
                                   s.visibility !== 'hidden' &&
                                   s.opacity !== '0';
                        };

                        let callback = false;
                        try {
                            if (window.ASPxGridView2 &&
                                typeof window.ASPxGridView2.InCallback === 'function') {
                                callback = !!window.ASPxGridView2.InCallback();
                            }
                        } catch (_) { }

                        return rows.length > 0 &&
                               !callback &&
                               !visible(lp) &&
                               !visible(ld);
                        """));
                }
                catch (WebDriverException)
                {
                    return false;
                }
            });
        }
        catch (WebDriverTimeoutException ex)
        {
            throw new WebDriverTimeoutException(
                "Кнопка #searchBtn была нажата, но после postback первая строка " +
                "ASPxGridView2_DXDataRow0 не появилась.", ex);
        }
    }
}
