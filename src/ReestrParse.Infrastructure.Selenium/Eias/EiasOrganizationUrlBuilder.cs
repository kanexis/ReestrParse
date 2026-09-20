using System.Text.RegularExpressions;
using ReestrParse.Domain.Catalog;

namespace ReestrParse.Infrastructure.Selenium.Eias;

internal static partial class EiasOrganizationUrlBuilder
{
    public static string? Build(
        RegionOption region,
        SphereOption sphere,
        string formValue,
        string organizationId)
    {
        if (string.IsNullOrWhiteSpace(region.ExternalId) ||
            string.IsNullOrWhiteSpace(sphere.ExternalId) ||
            string.IsNullOrWhiteSpace(formValue) ||
            string.IsNullOrWhiteSpace(organizationId))
        {
            return null;
        }

        return "https://ri.eias.ru/Discl/PublicDisclosureInfoOrg.aspx" +
               $"?reg={Uri.EscapeDataString(region.ExternalId)}" +
               $"&form={Uri.EscapeDataString(formValue)}" +
               "&razdel=null" +
               $"&sphere={Uri.EscapeDataString(sphere.ExternalId)}" +
               "&year=0&period=null" +
               $"&orgId={Uri.EscapeDataString(organizationId)}" +
               "&mo=&mr=";
    }

    public static string ExtractOrganizationId(string? detailUrl)
    {
        if (string.IsNullOrWhiteSpace(detailUrl))
            return string.Empty;

        var match = OrgIdRegex().Match(detailUrl);
        return match.Success ? match.Groups[1].Value : string.Empty;
    }

    [GeneratedRegex(@"[?&]orgId=(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex OrgIdRegex();
}
