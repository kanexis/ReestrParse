namespace ReestrParse.Application.Catalog;

public sealed record CatalogProgress(
    string Stage,
    int Page,
    int OrganizationsFound,
    string Message,
    int TotalPages = 0,
    int TotalOrganizations = 0,
    TimeSpan? Duration = null);
