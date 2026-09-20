using ReestrParse.Domain.Organizations;
using ReestrParse.Infrastructure.Selenium.Eias.Details;

namespace ReestrParse.Infrastructure.Selenium.Tests;

public sealed class EiasForm411ParserTests
{
    [Fact]
    public void Parse_ReadsStableParameterCodesAndMultiplePhones()
    {
        const string html = """
            <div id="sheets">
              <div>
                <table>
                  <tr><td class="row-number">2</td><td colspan="4">Форма 4.1.1 Общая информация об организации1</td></tr>
                  <tr><td class="row-number">12</td><td>2.1</td><td>Наименование</td><td>АО «Водоканал»</td><td></td></tr>
                  <tr><td class="row-number">13</td><td>2.2</td><td>ИНН</td><td>1435219600</td><td></td></tr>
                  <tr><td class="row-number">14</td><td>2.3</td><td>КПП</td><td>143501001</td><td></td></tr>
                  <tr><td class="row-number">20</td><td>3.1.1</td><td>Фамилия</td><td>Алексеева</td><td></td></tr>
                  <tr><td class="row-number">21</td><td>3.1.2</td><td>Имя</td><td>Наталья</td><td></td></tr>
                  <tr><td class="row-number">22</td><td>3.1.3</td><td>Отчество</td><td>Николаевна</td><td></td></tr>
                  <tr><td class="row-number">23</td><td>3.2</td><td>должность</td><td>корпоративный секретарь</td><td></td></tr>
                  <tr><td class="row-number">24</td><td>3.3</td><td>контактный телефон</td><td>(4112)211749</td><td></td></tr>
                  <tr><td class="row-number">25</td><td>3.4</td><td>адрес электронной почты</td><td><a>NataliAlex8@mail.ru</a></td><td></td></tr>
                  <tr><td class="row-number">33</td><td>7.1</td><td>контактный телефон</td><td>(4112)212141</td><td></td></tr>
                  <tr><td class="row-number">34</td><td>7.1</td><td>контактный телефон</td><td>(4112)212142</td><td></td></tr>
                  <tr><td class="row-number">35</td><td>8</td><td>Сайт</td><td>vodokanal-ykt.ru</td><td></td></tr>
                  <tr><td class="row-number">36</td><td>9</td><td>Email</td><td><a>yvdk@mail.ru</a></td><td></td></tr>
                </table>
              </div>
            </div>
            """;

        var source = new OrganizationReference(
            "26506945",
            "fallback",
            "fallback-inn",
            "fallback-kpp",
            "Республика Саха (Якутия)",
            "Теплоснабжение",
            "https://ri.eias.ru/detail",
            1,
            "26506945");

        var result = EiasForm411Parser.Parse(
            html,
            source,
            source.DetailUrl!,
            "https://ri-loader.eias.ru/template");

        Assert.Equal("АО «Водоканал»", result.Name);
        Assert.Equal("1435219600", result.Inn);
        Assert.Equal("143501001", result.Kpp);
        Assert.Equal(new[] { "(4112)212141", "(4112)212142" }, result.Phones);
        Assert.Equal("yvdk@mail.ru", result.Email);
        Assert.Equal("Алексеева Наталья Николаевна", result.ResponsibleFullName);
        Assert.Equal("(4112)211749", result.ResponsiblePhone);
        Assert.Equal("NataliAlex8@mail.ru", result.ResponsibleEmail);
    }
}
