using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using ReestrParse.Domain.Catalog;

namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Инкапсулирует всю работу с bootstrap-select регионов на Map.aspx.
/// EiasCatalogService не должен зависеть от конкретной HTML-разметки списка.
/// </summary>
internal static class EiasRegionSelector
{
    public static IReadOnlyList<RegionOption> ReadRegions(
        IWebDriver driver,
        WebDriverWait wait)
    {
        var items = wait.Until(d =>
        {
            var found = FindRegionItems(d);

            // Bootstrap-select держит пункты в DOM даже когда dropdown закрыт.
            // IWebElement.Text у скрытых span возвращает пустую строку, поэтому
            // ждём именно наличие textContent хотя бы у одного реального пункта.
            var hasRegionText = found.Any(x =>
                !HasClass(x, "disabled") &&
                !string.IsNullOrWhiteSpace(ReadItemText(x)));

            return hasRegionText ? found : null;
        }) ?? throw new WebDriverTimeoutException(
            "Bootstrap-список регионов ЕИАС не появился или не содержит названий регионов.");

        var nativeOptions = ReadNativeOptions(driver);
        var regions = new List<RegionOption>();

        foreach (var item in items)
        {
            if (HasClass(item, "disabled"))
                continue;

            var name = ReadItemText(item);

            if (string.IsNullOrWhiteSpace(name))
                continue;

            var rel = (item.GetAttribute("rel") ?? string.Empty).Trim();
            var externalId = ResolveExternalId(nativeOptions, rel, name);

            // В видимом bootstrap-списке numeric id региона отсутствует.
            // Если исходный select недоступен, сохраняем стабильный DOM-key.
            // Сам выбор региона всё равно выполняется по имени.
            if (string.IsNullOrWhiteSpace(externalId))
                externalId = string.IsNullOrWhiteSpace(rel) ? name : $"bootstrap:{rel}";

            regions.Add(new RegionOption(externalId, name));
        }

        return regions
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Name)
            .ToList();
    }

    public static void SelectRegion(
        IWebDriver driver,
        WebDriverWait wait,
        RegionOption region)
    {
        // Предпочтительный путь: скрытый native select, если он всё ещё есть.
        // Значение выставляем JS-ом, потому что bootstrap-select скрывает элемент.
        var nativeSelect = driver
            .FindElements(By.Id(EiasDomHints.RegionSelectId))
            .FirstOrDefault();

        if (nativeSelect is not null)
        {
            var options = nativeSelect.FindElements(By.TagName("option"));

            var option = options.FirstOrDefault(o =>
                    !region.ExternalId.StartsWith("bootstrap:", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        (o.GetAttribute("value") ?? string.Empty).Trim(),
                        region.ExternalId,
                        StringComparison.Ordinal))
                ?? options.FirstOrDefault(o =>
                    string.Equals(ReadElementText(o), region.Name, StringComparison.OrdinalIgnoreCase));

            if (option is not null)
            {
                var value = (option.GetAttribute("value") ?? string.Empty).Trim();

                if (!string.IsNullOrWhiteSpace(value))
                {
                    driver.SetBootstrapSelectValue(nativeSelect, value);
                    return;
                }
            }
        }

        // Fallback: кликаем по тому же bootstrap-списку, который видит пользователь.
        var toggle = driver
            .FindElements(By.CssSelector(EiasDomHints.RegionToggleCss))
            .FirstOrDefault(x => x.Displayed && x.Enabled);

        if (toggle is not null)
            toggle.Click();

        var item = wait.Until(d => FindRegionItems(d)
            .FirstOrDefault(x =>
            {
                if (HasClass(x, "disabled"))
                    return false;

                var text = ReadItemText(x);

                return string.Equals(text, region.Name, StringComparison.OrdinalIgnoreCase);
            }));

        if (item is null)
        {
            throw new NoSuchElementException(
                $"Регион '{region.Name}' отсутствует в bootstrap-списке ЕИАС.");
        }

        var anchor = item.FindElements(By.CssSelector("a")).FirstOrDefault()
            ?? throw new NoSuchElementException(
                $"Для региона '{region.Name}' не найдена ссылка выбора.");

        if (anchor.Displayed && anchor.Enabled)
        {
            anchor.Click();
        }
        else
        {
            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", anchor);
        }
    }

    private static List<IWebElement> FindRegionItems(IWebDriver driver)
    {
        // Сначала более точный контейнер региона, затем fallback на структуру
        // из фактического bootstrap-select HTML.
        var scoped = driver
            .FindElements(By.CssSelector(
                "#reg-select-container ul.dropdown-menu.inner.selectpicker > li"))
            .ToList();

        if (scoped.Count > 0)
            return scoped;

        return driver
            .FindElements(By.CssSelector("ul.dropdown-menu.inner.selectpicker > li"))
            .ToList();
    }

    private static List<NativeRegionOption> ReadNativeOptions(IWebDriver driver)
    {
        var select = driver
            .FindElements(By.Id(EiasDomHints.RegionSelectId))
            .FirstOrDefault();

        if (select is null)
            return [];

        return select
            .FindElements(By.TagName("option"))
            .Select((option, index) => new NativeRegionOption(
                index,
                (option.GetAttribute("value") ?? string.Empty).Trim(),
                ReadElementText(option)))
            .ToList();
    }

    private static string? ResolveExternalId(
        IReadOnlyList<NativeRegionOption> nativeOptions,
        string rel,
        string name)
    {
        if (int.TryParse(rel, out var index))
        {
            var byIndex = nativeOptions.FirstOrDefault(x => x.Index == index);
            if (byIndex is not null && !string.IsNullOrWhiteSpace(byIndex.Value))
                return byIndex.Value;
        }

        return nativeOptions
            .FirstOrDefault(x =>
                string.Equals(x.Text, name, StringComparison.OrdinalIgnoreCase))
            ?.Value;
    }


    private static string ReadItemText(IWebElement item)
    {
        var textElement = item
            .FindElements(By.CssSelector(EiasDomHints.RegionItemTextCss))
            .FirstOrDefault();

        return ReadElementText(textElement ?? item);
    }

    private static string ReadElementText(IWebElement element)
    {
        // Важно: .Text возвращает только отображаемый текст. Dropdown bootstrap-select
        // обычно закрыт, поэтому span.text присутствует в DOM, но Displayed == false.
        // textContent читается независимо от видимости элемента.
        var text = element.GetAttribute("textContent") ?? element.Text ?? string.Empty;

        return string.Join(" ", text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static bool HasClass(IWebElement element, string cssClass)
    {
        var classes = (element.GetAttribute("class") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return classes.Contains(cssClass, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record NativeRegionOption(int Index, string Value, string Text);
}
