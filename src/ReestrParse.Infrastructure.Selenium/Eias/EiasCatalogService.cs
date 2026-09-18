using System.Text.RegularExpressions;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
using ReestrParse.Application.Catalog;
using ReestrParse.Domain.Catalog;
using ReestrParse.Domain.Organizations;
using ReestrParse.Infrastructure.Selenium.Browser;

namespace ReestrParse.Infrastructure.Selenium.Eias;

internal sealed class EiasCatalogService(SeleniumBrowserSession browser) : IEiasCatalogService
{
    private static readonly Regex OrgIdRegex =
        new(@"(?:orgId=|organizationId=)(?<id>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DetailUrlRegex =
        new(@"(?<url>(?:https?://[^'""\s]+)?/?Discl/PublicDisclosureInfoOrg\.aspx\?[^'""\s]+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task<IReadOnlyList<RegionOption>> LoadRegionsAsync(CancellationToken cancellationToken)
        => browser.RunAsync<IReadOnlyList<RegionOption>>((driver, wait) =>
        {
            driver.Navigate().GoToUrl(EiasUrls.Map);
            WaitForDocument(driver, wait);

            var select = driver.FindBestSelect(EiasDomHints.RegionSelectCss, "reg");
            var options = new SelectElement(select).Options
                .Select(o => new RegionOption(
                    (o.GetAttribute("value") ?? string.Empty).Trim(),
                    o.Text.Trim()))
                .Where(x =>
                    !string.IsNullOrWhiteSpace(x.ExternalId) &&
                    !string.IsNullOrWhiteSpace(x.Name) &&
                    !x.Name.Contains("выберите", StringComparison.OrdinalIgnoreCase))
                .DistinctBy(x => x.ExternalId)
                .OrderBy(x => x.Name)
                .ToList();

            return options;
        }, cancellationToken);

    public Task<IReadOnlyList<OrganizationReference>> LoadOrganizationsAsync(
        RegionOption region,
        SphereOption sphere,
        IProgress<CatalogProgress>? progress,
        CancellationToken cancellationToken)
        => browser.RunAsync<IReadOnlyList<OrganizationReference>>((driver, wait) =>
        {
            driver.Navigate().GoToUrl(EiasUrls.Map);
            WaitForDocument(driver, wait);

            progress?.Report(new("Выбор региона", 0, 0, region.Name));

            var regionSelectElement = driver.FindBestSelect(EiasDomHints.RegionSelectCss, "reg");
            var regionSelect = new SelectElement(regionSelectElement);
            regionSelect.SelectByValue(region.ExternalId);
            driver.DispatchChange(regionSelectElement);

            // Даём динамической части страницы отреагировать.
            Thread.Sleep(500);

            TrySelectHeatSupply(driver, sphere);
            driver.ClickButtonByText(EiasDomHints.SearchButtonTexts);

            WaitForDocument(driver, wait);
            Thread.Sleep(700);

            var collected = new Dictionary<string, OrganizationReference>(StringComparer.OrdinalIgnoreCase);
            var page = 1;
            var safety = 0;

            while (safety++ < 500)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var org in ExtractOrganizations(driver, region, sphere, page))
                {
                    var key = !string.IsNullOrWhiteSpace(org.ExternalId)
                        ? org.ExternalId
                        : (org.DetailUrl ?? org.Name);

                    collected.TryAdd(key, org);
                }

                progress?.Report(new(
                    "Чтение организаций",
                    page,
                    collected.Count,
                    $"Страница {page}: найдено {collected.Count}"));

                var before = BuildListSignature(driver);
                var next = FindNextPage(driver);

                if (next is null)
                    break;

                next.Click();

                wait.Until(d =>
                {
                    try
                    {
                        return BuildListSignature(d) != before;
                    }
                    catch (StaleElementReferenceException)
                    {
                        return true;
                    }
                });

                page++;
            }

            return collected.Values
                .OrderBy(x => x.Name)
                .ToList();
        }, cancellationToken);

    private static void TrySelectHeatSupply(IWebDriver driver, SphereOption sphere)
    {
        IWebElement? sphereElement = null;

        try
        {
            sphereElement = driver.FindBestSelect(EiasDomHints.SphereSelectCss, "spher");
        }
        catch (NoSuchElementException)
        {
            // На некоторых версиях страницы сфера может задаваться не select.
        }

        if (sphereElement is null)
            return;

        var select = new SelectElement(sphereElement);
        var option = select.Options.FirstOrDefault(o =>
            o.Text.Contains("тепл", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(o.GetAttribute("value"), sphere.ExternalId, StringComparison.OrdinalIgnoreCase));

        if (option is null)
            return;

        option.Click();
        driver.DispatchChange(sphereElement);
    }

    private static IEnumerable<OrganizationReference> ExtractOrganizations(
        IWebDriver driver,
        RegionOption region,
        SphereOption sphere,
        int page)
    {
        var anchors = driver.FindElements(By.CssSelector("a"));

        foreach (var anchor in anchors)
        {
            if (!anchor.Displayed)
                continue;

            var name = anchor.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var href = anchor.GetAttribute("href") ?? string.Empty;
            var onclick = anchor.GetAttribute("onclick") ?? string.Empty;
            var raw = href + " " + onclick;

            if (!raw.Contains("PublicDisclosureInfoOrg", StringComparison.OrdinalIgnoreCase) &&
                !raw.Contains("orgId", StringComparison.OrdinalIgnoreCase))
                continue;

            var detailUrl = NormalizeDetailUrl(driver.Url, href, onclick);
            var orgId = ExtractOrgId(raw + " " + detailUrl) ?? name;

            yield return new OrganizationReference(
                orgId,
                name,
                region.Name,
                sphere.Name,
                detailUrl,
                page);
        }
    }

    private static string? NormalizeDetailUrl(string currentUrl, string href, string onclick)
    {
        if (Uri.TryCreate(href, UriKind.Absolute, out var absolute))
            return absolute.ToString();

        if (!string.IsNullOrWhiteSpace(href) &&
            Uri.TryCreate(new Uri(currentUrl), href, out var relative))
            return relative.ToString();

        var match = DetailUrlRegex.Match(onclick);
        if (!match.Success)
            return null;

        var value = match.Groups["url"].Value
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase);

        if (Uri.TryCreate(value, UriKind.Absolute, out absolute))
            return absolute.ToString();

        return Uri.TryCreate(new Uri(currentUrl), value, out relative)
            ? relative.ToString()
            : null;
    }

    private static string? ExtractOrgId(string text)
    {
        var match = OrgIdRegex.Match(text);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private static IWebElement? FindNextPage(IWebDriver driver)
    {
        var candidates = driver.FindElements(By.CssSelector("a, button, input[type='button']"))
            .Where(x => x.Displayed && x.Enabled)
            .ToList();

        return candidates.FirstOrDefault(x =>
        {
            var text = (x.Text + " " + x.GetAttribute("title") + " " + x.GetAttribute("aria-label")).Trim();
            var rel = x.GetAttribute("rel");

            return string.Equals(rel, "next", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals(">", StringComparison.OrdinalIgnoreCase) ||
                   text.Equals("»", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("след", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("next", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string BuildListSignature(IWebDriver driver)
    {
        var items = driver.FindElements(By.CssSelector("a"))
            .Where(x => x.Displayed)
            .Select(x => (x.Text + "|" + x.GetAttribute("href")).Trim())
            .Where(x => x.Contains("PublicDisclosureInfoOrg", StringComparison.OrdinalIgnoreCase) ||
                        x.Contains("orgId", StringComparison.OrdinalIgnoreCase))
            .Take(5);

        return string.Join(";", items);
    }

    private static void WaitForDocument(IWebDriver driver, WebDriverWait wait)
    {
        wait.Until(d =>
            ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString() == "complete");
    }
}
