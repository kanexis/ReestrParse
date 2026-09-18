using OpenQA.Selenium;

namespace ReestrParse.Infrastructure.Selenium.Eias;

internal static class SeleniumDomExtensions
{
     ///<summary>
     /// Ищет элемент независимо от Displayed.Это важно для Bootstrap Select:
     /// настоящий select присутствует в DOM, но скрыт через display:none.
     ///</summary>
    public static IWebElement FindRequired(this IWebDriver driver, By by, string description)
    {
        var element = driver.FindElements(by).FirstOrDefault();

        return element ?? throw new NoSuchElementException(
            $"Не найден элемент '{description}' ({by}).");
    }

    public static IWebElement FindBestSelect(
        this IWebDriver driver,
        IEnumerable<string> cssSelectors,
        string semanticText)
    {
        // Сначала ищем точные CSS-селекторы БЕЗ проверки Displayed.
        // Bootstrap Select специально скрывает исходные <select>.
        foreach (var css in cssSelectors)
        {
            var exact = driver.FindElements(By.CssSelector(css)).FirstOrDefault();
            if (exact is not null)
                return exact;
        }

        var selects = driver.FindElements(By.TagName("select")).ToList();

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

    /// <summary>
    /// Выставляет значение скрытого Bootstrap Select и синхронизирует визуальный selectpicker.
    /// Обычный Selenium Click по option здесь ненадёжен, потому что исходный select скрыт.
    /// </summary>
    public static void SetBootstrapSelectValue(
        this IWebDriver driver,
        IWebElement select,
        string value)
    {
        var js = (IJavaScriptExecutor)driver;

        js.ExecuteScript(
            """
            const select = arguments[0];
            const value = arguments[1];

            select.value = value;

            if (window.jQuery) {
                const $select = window.jQuery(select);

                if (typeof $select.selectpicker === 'function') {
                    $select.selectpicker('val', value);
                    $select.selectpicker('refresh');
                }

                $select.trigger('change');
            } else {
                select.dispatchEvent(new Event('change', { bubbles: true }));
            }
            """,
            select,
            value);
    }
}
