using ReestrParse.Domain.Catalog;

namespace ReestrParse.Application.Tests;

public sealed class CatalogModelsTests
{
    [Fact]
    public void HeatSupply_HasExpectedCaption()
    {
        Assert.Equal("Теплоснабжение", SphereOption.HeatSupply.Name);
        Assert.Equal("WARM", SphereOption.HeatSupply.ExternalId);
    }
}
