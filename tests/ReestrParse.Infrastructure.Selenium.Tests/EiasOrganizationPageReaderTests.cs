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
}
