using AngleSharp.Html.Parser;
using ReestrParse.Infrastructure.Selenium.Eias.Details;
using Xunit;

namespace ReestrParse.Infrastructure.Selenium.Tests;

public sealed class EiasForm101ParserTests
{
    [Fact]
    public void ParseTable_ReadsActivityAndTerritoryByStableCodes()
    {
        const string html = """
            <div id="sheets">
              <div>
                <table>
                  <tr><td class="row-number">2</td><td colspan="4">Форма 1.0.1 Основные параметры раскрываемой информации 1</td></tr>
                  <tr><td class="row-number">5</td><td>1</td><td>Дата заполнения/внесения изменений</td><td>25.10.2018</td><td></td></tr>
                  <tr><td class="row-number">6</td><td>2.1</td><td>Система</td><td>Тепловые сети №1</td><td></td></tr>
                  <tr><td class="row-number">7</td><td>3.1</td><td>Вид деятельности</td><td>Передача. Тепловая энергия</td><td></td></tr>
                  <tr><td class="row-number">9</td><td>4.1.1</td><td>Субъект РФ</td><td>Алтайский край</td><td></td></tr>
                  <tr><td class="row-number">10</td><td>4.1.1.1</td><td>Район</td><td>Тестовый район</td><td></td></tr>
                  <tr><td class="row-number">11</td><td>4.1.1.1.1</td><td>МО</td><td>Тестовое МО</td><td></td></tr>
                </table>
              </div>
            </div>
            """;

        var document = new HtmlParser().ParseDocument(html);
        var table = EiasForm101Parser.FindTable(document);

        Assert.NotNull(table);
        var result = EiasForm101Parser.ParseTable(table!);

        Assert.Equal("25.10.2018", result.DisclosureUpdatedAt);
        Assert.Equal(new[] { "Тепловые сети №1" }, result.InfrastructureSystems);
        Assert.Equal(new[] { "Передача. Тепловая энергия" }, result.RegulatedActivities);
        Assert.Equal(new[] { "Алтайский край" }, result.ServiceRegions);
        Assert.Equal(new[] { "Тестовый район" }, result.MunicipalDistricts);
        Assert.Equal(new[] { "Тестовое МО" }, result.Municipalities);
    }
}
