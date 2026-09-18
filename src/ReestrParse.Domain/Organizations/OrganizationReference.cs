namespace ReestrParse.Domain.Organizations;

public sealed record OrganizationReference(
    string ExternalId,
    string Name,
    string RegionName,
    string SphereName,
    string? DetailUrl,
    int SourcePage);
