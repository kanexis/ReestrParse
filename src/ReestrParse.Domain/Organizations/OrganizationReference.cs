namespace ReestrParse.Domain.Organizations;

public sealed record OrganizationReference(
    string ExternalId,
    string Name,
    string Inn,
    string Kpp,
    string RegionName,
    string SphereName,
    string? DetailUrl,
    int SourcePage,
    string OrganizationId = "",
    string RegionId = "",
    string SphereId = "",
    string FormValue = "");
