using ReestrParse.Domain.Organizations;

namespace ReestrParse.Application.Details;

public sealed record DetailsProgress(
    int Completed,
    int Total,
    int Succeeded,
    int Failed,
    int Partial,
    OrganizationReference Organization,
    OrganizationContactDetails? Details,
    string? Error,
    int WorkerId = 0,
    TimeSpan? Duration = null,
    string Step = "")
{
    public string Message => Error is not null
        ? $"[{Completed}/{Total}] {Organization.Name} — {Error}"
        : Details?.IsPartial == true
            ? $"[{Completed}/{Total}] {Organization.Name} — частично: форма 1.0.1 без 4.1.1"
            : $"[{Completed}/{Total}] {Organization.Name} — контакты получены";
}
