using ReestrParse.Infrastructure.Selenium.Eias.Details;
using Xunit;

namespace ReestrParse.Infrastructure.Selenium.Tests;

public sealed class EiasOrganizationPageReaderTests
{
    [Fact]
    public void ExtractForm411TemplateUrl_SelectsOnlyForm411()
    {
        const string html = """
            <table id="ASPxGridViewDet_DXMainTable">
              <tr id="ASPxGridViewDet_DXDataRow0">
                <td></td><td>Теплоснабжение</td><td>Сведения</td>
                <td>4.1.1</td><td>Общая информация об организации</td><td>Единоразовый</td>
                <td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td>
                <td><a class="get_template" onclick="openTemplateDialog('https://ri-loader.eias.ru/TemplatePrinter.aspx?reg=RU.7.14&amp;guid=abc&amp;id=123', 'АО Тест');return false;">open</a></td>
              </tr>
              <tr id="ASPxGridViewDet_DXDataRow1">
                <td></td><td>Теплоснабжение</td><td>Сведения</td>
                <td>5.1.1</td><td>Другая форма</td><td>2026</td>
                <td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td>
                <td><a class="get_template" onclick="openTemplateDialog('https://example.org/wrong', 'wrong');return false;">open</a></td>
              </tr>
            </table>
            """;

        var url = EiasOrganizationPageReader.ExtractForm411TemplateUrl(
            html,
            new Uri("https://ri.eias.ru/Discl/PublicDisclosureInfoOrg.aspx"));

        Assert.Equal(
            "https://ri-loader.eias.ru/TemplatePrinter.aspx?reg=RU.7.14&guid=abc&id=123",
            url);
    }

    [Fact]
    public void ExtractTemplateCandidates_UsesForm101AsFallbackCandidate()
    {
        const string html = """
            <table id="ASPxGridViewDet_DXMainTable">
              <tr id="ASPxGridViewDet_DXDataRow0">
                <td></td><td>Теплоснабжение</td><td>Сведения</td>
                <td>1.0.1</td><td>Основные параметры раскрываемой информации</td><td>Единоразовый</td>
                <td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td>
                <td><a class="get_template" onclick="openTemplateDialog('https://ri-loader.eias.ru/TemplatePrinter.aspx?guid=form1&amp;id=7', 'ООО Тест');return false;">open</a></td>
                <td>20.09.2026</td>
              </tr>
            </table>
            """;

        var candidates = EiasOrganizationPageReader.ExtractTemplateCandidates(
            html,
            new Uri("https://ri.eias.ru/Discl/PublicDisclosureInfoOrg.aspx"));

        var candidate = Assert.Single(candidates);
        Assert.Equal("1.0.1", candidate.FormNumber);
        Assert.Equal(10, candidate.Priority);
        Assert.Contains("TemplatePrinter.aspx", candidate.TemplateUrl);
    }
}
