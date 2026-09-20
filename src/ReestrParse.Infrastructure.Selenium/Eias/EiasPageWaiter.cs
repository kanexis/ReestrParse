using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ReestrParse.Infrastructure.Selenium.Eias;

internal static class EiasPageWaiter
{
    public static void WaitForDocumentReady(IWebDriver driver, WebDriverWait wait)
    {
        wait.Until(d => ExecuteBool(d, "return document.readyState === 'complete';"));
    }

    public static void WaitForSearchPageReady(IWebDriver driver, WebDriverWait wait)
    {
        wait.Until(d => ExecuteBool(d, """
            if (document.readyState !== 'complete') return false;

            const search = document.getElementById('searchBtn');
            const form = document.getElementById('ui-multiselect-FormSelect-option-10')
                || document.querySelector("input[name='multiselect_FormSelect'][value*='F_W_O_4_1_1']");

            return !!search && !!form;
            """));
    }

    public static bool ExecuteBool(IWebDriver driver, string script, params object[] args)
    {
        try
        {
            driver.SwitchTo().DefaultContent();
            var value = ((IJavaScriptExecutor)driver).ExecuteScript(script, args);
            return Convert.ToBoolean(value);
        }
        catch (WebDriverException)
        {
            return false;
        }
    }
}
