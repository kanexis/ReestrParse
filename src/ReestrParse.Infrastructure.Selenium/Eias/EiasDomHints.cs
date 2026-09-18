namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Единственная точка, которую надо адаптировать, если ЕИАС меняет DOM.
/// Селекторы расположены от наиболее точных к более общим.
/// </summary>
internal static class EiasDomHints
{
    public static readonly string[] RegionSelectCss =
    [
        "select[id*='Region' i]",
        "select[name*='Region' i]",
        "select[id*='reg' i]",
        "select[name*='reg' i]"
    ];

    public static readonly string[] SphereSelectCss =
    [
        "select[id*='Sphere' i]",
        "select[name*='Sphere' i]",
        "select[id*='sfer' i]",
        "select[name*='sfer' i]"
    ];

    public static readonly string[] SearchButtonTexts =
    [
        "Показать",
        "Найти",
        "Выбрать",
        "Применить",
        "Перейти"
    ];
}
