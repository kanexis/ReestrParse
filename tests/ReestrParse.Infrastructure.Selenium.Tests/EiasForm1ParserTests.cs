using ReestrParse.Domain.Organizations;
using ReestrParse.Infrastructure.Selenium.Eias.Details;
using Xunit;

namespace ReestrParse.Infrastructure.Selenium.Tests;

public sealed class EiasForm1ParserTests
{
    [Fact]
    public void ParseValues_ReadsCurrentHeatSupplyForm1Codes()
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["1"] = ["ООО Тепло"],
            ["6.1"] = ["Иванов"],
            ["6.2"] = ["Иван"],
            ["6.3"] = ["Иванович"],
            ["7"] = ["656000, г. Барнаул, а/я 1"],
            ["8"] = ["г. Барнаул, ул. Тестовая, 1"],
            ["9"] = ["+7 3852 00-00-01", "+7 3852 00-00-02"],
            ["10"] = ["https://teplo.example"],
            ["11"] = ["mail@teplo.example"]
        };

        var source = new OrganizationReference(
            "42", "fallback", "2222000000", "222201001", "Алтайский край",
            "Теплоснабжение", null, 1);

        var details = EiasForm1Parser.ParseValues(
            values,
            source,
            "https://ri.eias.ru/detail",
            "https://ri-loader.eias.ru/template");

        Assert.True(details.HasForm1);
        Assert.False(details.HasForm411);
        Assert.Equal("ООО Тепло", details.Name);
        Assert.Equal("Иванов Иван Иванович", details.ManagerFullName);
        Assert.Equal("656000, г. Барнаул, а/я 1", details.PostalAddress);
        Assert.Equal("г. Барнаул, ул. Тестовая, 1", details.LocationAddress);
        Assert.Equal(new[] { "+7 3852 00-00-01", "+7 3852 00-00-02" }, details.Phones);
        Assert.Equal("https://teplo.example", details.Website);
        Assert.Equal("mail@teplo.example", details.Email);
        Assert.Equal("1", details.ParsedForms);
        Assert.False(details.IsPartial);
    }
}
