using ReestrParse.Domain.Catalog;
using Xunit;

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
