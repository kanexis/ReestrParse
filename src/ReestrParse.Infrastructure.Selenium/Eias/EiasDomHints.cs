namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Точные DOM-ориентиры основной страницы ФГИС ЕИАС.
/// Bootstrap Select скрывает настоящий select, поэтому для региона
/// мы обращаемся непосредственно к #region-select, а не к визуальному dropdown.
/// </summary>
internal static class EiasDomHints
{
    public const string RegionSelectId = "region-select";
    public const string RegionSelectCss = "#region-select";
    public const string RegionOptionCss = "#region-select option";
    public const string GoButtonId = "go-btn";

    // Сферу уточним по фактической разметке следующего экрана.
    // Пока оставляем fallback-набор, чтобы не связывать UI с DOM сайта.
    public static readonly string[] SphereSelectCss =
    [
        "select[id*='Sphere' i]",
        "select[name*='Sphere' i]",
        "select[id*='sfer' i]",
        "select[name*='sfer' i]"
    ];
}
