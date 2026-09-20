using ReestrParse.Domain.Organizations;

namespace ReestrParse.Application.Details;

public interface IEiasOrganizationDetailsService
{
    Task<IReadOnlyList<OrganizationDetailsResult>> LoadDetailsAsync(
        IReadOnlyList<OrganizationReference> organizations,
        DetailsCrawlOptions options,
        IProgress<DetailsProgress>? progress,
        CancellationToken cancellationToken);
}
