using ReestrParse.Domain.Organizations;

namespace ReestrParse.Application.Details;

public sealed record DetailsProgress(
    int Completed,
    int Total,
    int Succeeded,
    int Failed,
    OrganizationReference Organization,
    OrganizationContactDetails? Details,
    string? Error,
    int WorkerId = 0,
    TimeSpan? Duration = null,
    string Step = "")
{
    public string Message => Error is null
        ? $"[{Completed}/{Total}] {Organization.Name} — контакты получены"
        : $"[{Completed}/{Total}] {Organization.Name} — {Error}";
}
