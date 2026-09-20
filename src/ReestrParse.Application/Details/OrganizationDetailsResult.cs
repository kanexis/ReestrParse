using ReestrParse.Domain.Organizations;

namespace ReestrParse.Application.Details;

public sealed record OrganizationDetailsResult(
    OrganizationReference Organization,
    OrganizationContactDetails? Details,
    string? Error)
{
    public bool IsSuccess => Details is not null && string.IsNullOrWhiteSpace(Error);
    public bool IsPartial => IsSuccess && Details!.IsPartial;
    public bool IsFull => IsSuccess && !IsPartial;
}
