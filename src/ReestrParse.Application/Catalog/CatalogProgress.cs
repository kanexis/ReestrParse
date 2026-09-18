namespace ReestrParse.Application.Catalog;

public sealed record CatalogProgress(
    string Stage,
    int Page,
    int OrganizationsFound,
    string Message);
