using ReestrParse.Domain.Catalog;
using ReestrParse.Domain.Organizations;
using ReestrParse.Infrastructure.Selenium.Eias;
using Xunit;

namespace ReestrParse.Infrastructure.Selenium.Tests;

public sealed class EiasOrganizationUrlBuilderTests
{
    [Fact]
    public void BuildOrganizationUrl_ContainsOrgId()
    {
        var url = EiasOrganizationUrlBuilder.Build(
            new RegionOption("2663", "Республика Саха (Якутия)"),
            new SphereOption("WARM", "Теплоснабжение"),
            "F_W_O_1;F_W_O_4_1_1;",
            "26506945");

        Assert.NotNull(url);
        Assert.Contains("orgId=26506945", url, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("26506945", EiasOrganizationUrlBuilder.ExtractOrganizationId(url));
    }

    [Fact]
    public void BuildCatalogUrl_ContainsCurrentFilterState()
    {
        var reference = new OrganizationReference(
            ExternalId: "2901125778",
            Name: "АО test",
            Inn: "2901125778",
            Kpp: "292101001",
            RegionName: "Архангельская область",
            SphereName: "Теплоснабжение",
            DetailUrl: null,
            SourcePage: 2,
            OrganizationId: "",
            RegionId: "2607",
            SphereId: "WARM",
            FormValue: "F_W_O_1;F_W_O_4_1_1;");

        var url = EiasCatalogUrlBuilder.Build(reference);

        Assert.NotNull(url);
        Assert.Contains("reg=2607", url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sphere=WARM", url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("orgreg=false", url, StringComparison.OrdinalIgnoreCase);
    }
}
