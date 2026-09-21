using ReestrParse.Domain.Organizations;
using ReestrParse.Infrastructure.Selenium.Eias.Details;
using Xunit;

namespace ReestrParse.Infrastructure.Selenium.Tests;

public sealed class EiasTemplateWorkbookParserTests
{
    [Fact]
    public void ParseRuntime_ExplicitForm1Candidate_WinsOverOverlapping411Codes()
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["1"] = ["ООО Теплосеть"],
            // Эти коды пересекаются с 4.1.1 и раньше ошибочно перетягивали classification.
            ["2.2"] = ["служебное значение"],
            ["3.3"] = ["служебное значение"],
            ["3.4"] = ["служебное значение"],
            ["7"] = ["656000, г. Барнаул"],
            ["8"] = ["г. Барнаул, ул. Тепловая, 1"],
            ["9"] = ["+7 3852 00-00-01"],
            ["10"] = ["teplo.example.ru"],
            ["11"] = ["mail@teplo.example.ru"]
        };

        var source = new OrganizationReference(
            "42",
            "ООО Теплосеть",
            "2222000000",
            "222201001",
            "Алтайский край",
            "Теплоснабжение",
            null,
            1);

        var candidate = new EiasTemplateCandidate(
            "1",
            "Общая информация об организации",
            "https://ri-loader.eias.ru/template",
            "",
            2);

        var result = EiasTemplateWorkbookParser.ParseRuntime(
            values,
            candidate,
            source,
            "https://ri.eias.ru/detail",
            candidate.TemplateUrl);

        Assert.NotNull(result.Details);
        Assert.True(result.HasForm1);
        Assert.False(result.HasForm411);
        Assert.Equal("+7 3852 00-00-01", Assert.Single(result.Details!.Phones));
        Assert.Equal("mail@teplo.example.ru", result.Details.Email);
    }
}
