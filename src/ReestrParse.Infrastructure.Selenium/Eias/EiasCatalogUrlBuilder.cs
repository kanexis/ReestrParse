using ReestrParse.Domain.Organizations;

namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Строит прямой URL уже отфильтрованного каталога организаций. Используется details fallback,
/// когда DevExpress не раскрывает внутренний orgId и карточку приходится открывать реальным кликом строки.
/// </summary>
internal static class EiasCatalogUrlBuilder
{
    public static string? Build(OrganizationReference organization)
    {
        if (string.IsNullOrWhiteSpace(organization.RegionId) ||
            string.IsNullOrWhiteSpace(organization.SphereId) ||
            string.IsNullOrWhiteSpace(organization.FormValue))
        {
            return null;
        }

        return "https://ri.eias.ru/Discl/PublicDisclosureInfo.aspx" +
               $"?reg={Uri.EscapeDataString(organization.RegionId)}" +
               $"&form={Uri.EscapeDataString(organization.FormValue)}" +
               "&orgreg=false" +
               "&razdel=null" +
               $"&sphere={Uri.EscapeDataString(organization.SphereId)}" +
               "&year=0&period=null&mo=&mr=";
    }
}
