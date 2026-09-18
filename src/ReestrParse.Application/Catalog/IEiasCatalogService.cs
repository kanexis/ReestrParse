using ReestrParse.Domain.Catalog;
using ReestrParse.Domain.Organizations;

namespace ReestrParse.Application.Catalog;

public interface IEiasCatalogService
{
    Task<IReadOnlyList<RegionOption>> LoadRegionsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<OrganizationReference>> LoadOrganizationsAsync(
        RegionOption region,
        SphereOption sphere,
        IProgress<CatalogProgress>? progress,
        CancellationToken cancellationToken);
}
