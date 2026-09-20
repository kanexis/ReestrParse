namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Стабильные DOM-ориентиры ФГИС ЕИАС. Все конкретные id/css сайта держим здесь,
/// чтобы при изменении разметки не трогать Application/WPF.
/// </summary>
internal static class EiasDomHints
{
    public const string RegionSelectId = "region-select";

    public const string RegionListCss =
        "#reg-select-container ul.dropdown-menu.inner.selectpicker, ul.dropdown-menu.inner.selectpicker";

    public const string RegionListItemCss =
        "#reg-select-container ul.dropdown-menu.inner.selectpicker > li, ul.dropdown-menu.inner.selectpicker > li";

    public const string RegionItemTextCss = "span.text";

    public const string RegionToggleCss =
        "#reg-select-container button[data-id='region-select'], button[data-id='region-select']";

    public const string GoButtonId = "go-btn";
    public const string SearchButtonId = "searchBtn";

    // Сферу выбираем ТОЛЬКО внутри управляющих элементов сферы.
    // Никакого глобального поиска текста "Теплоснабжение" по странице: иначе можно
    // случайно выбрать организацию вроде "МУП Теплоснабжение".
    public const string HeatSphereValue = "WARM";

    public static readonly string[] SphereSelectCss =
    [
        "select[id*='Sphere' i]",
        "select[name*='Sphere' i]",
        "select[id*='sfer' i]",
        "select[name*='sfer' i]"
    ];

    public static readonly string[] SphereInputCss =
    [
        "input[type='checkbox'][value='WARM']",
        "input[type='radio'][value='WARM']",
        "input[type='checkbox'][name*='Sphere' i]",
        "input[type='radio'][name*='Sphere' i]",
        "input[type='checkbox'][name*='sfer' i]",
        "input[type='radio'][name*='sfer' i]",
        "input[type='checkbox'][id*='Sphere' i]",
        "input[type='radio'][id*='Sphere' i]",
        "input[type='checkbox'][id*='sfer' i]",
        "input[type='radio'][id*='sfer' i]"
    ];

    // Фильтр форм: "Общая информация об организации".
    public const string GeneralOrganizationFormCheckboxId =
        "ui-multiselect-FormSelect-option-10";

    public const string GeneralOrganizationFormValueContains =
        "F_W_O_4_1_1";

    // DevExpress GridView со списком организаций.
    public const string OrganizationsGridId = "ASPxGridView2";
    public const string OrganizationsGridMainTableId = "ASPxGridView2_DXMainTable";
    public const string OrganizationsPagerId = "ASPxGridView2_DXPagerBottom";
    public const string OrganizationsLoadingPanelId = "ASPxGridView2_LP";
    public const string OrganizationsLoadingDivId = "ASPxGridView2_LD";

    public const string OrganizationRowsCss =
        "#ASPxGridView2_DXMainTable tr[id^='ASPxGridView2_DXDataRow']";

    public const string CurrentPageCss =
        "#ASPxGridView2_DXPagerBottom .dxp-current";

    public const string PagerSummaryCss =
        "#ASPxGridView2_DXPagerBottom .dxp-summary";
}
