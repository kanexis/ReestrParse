using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ReestrParse.Infrastructure.Selenium.Eias;

internal static class SeleniumDomExtensions
{
    public static IWebElement? FindFirstDisplayed(this IWebDriver driver, IEnumerable<string> cssSelectors)
    {
        foreach (var css in cssSelectors)
        {
            var element = driver.FindElements(By.CssSelector(css)).FirstOrDefault(x => x.Displayed);
            if (element is not null)
                return element;
        }

        return null;
    }

    public static IWebElement FindBestSelect(this IWebDriver driver, IEnumerable<string> cssSelectors, string semanticText)
    {
        var exact = driver.FindFirstDisplayed(cssSelectors);
        if (exact is not null)
            return exact;

        var selects = driver.FindElements(By.TagName("select")).Where(x => x.Displayed).ToList();

        if (selects.Count == 1)
            return selects[0];

        foreach (var select in selects)
        {
            var scoreText = string.Join(" ",
                select.GetAttribute("id"),
                select.GetAttribute("name"),
                select.GetAttribute("aria-label"),
                select.GetAttribute("title"));

            if (scoreText.Contains(semanticText, StringComparison.OrdinalIgnoreCase))
                return select;
        }

        throw new NoSuchElementException($"Не найден выпадающий список '{semanticText}'.");
    }

    public static void DispatchChange(this IWebDriver driver, IWebElement element)
    {
        ((IJavaScriptExecutor)driver).ExecuteScript(
            "arguments[0].dispatchEvent(new Event('change', { bubbles: true }));", element);
    }

    public static bool ClickButtonByText(this IWebDriver driver, IEnumerable<string> texts)
    {
        var candidates = driver.FindElements(By.CssSelector("button, input[type='button'], input[type='submit'], a"));

        foreach (var text in texts)
        {
            var element = candidates.FirstOrDefault(x =>
            {
                var caption = (x.Text + " " + x.GetAttribute("value") + " " + x.GetAttribute("title")).Trim();
                return x.Displayed && caption.Contains(text, StringComparison.OrdinalIgnoreCase);
            });

            if (element is not null)
            {
                element.Click();
                return true;
            }
        }

        return false;
    }
}
