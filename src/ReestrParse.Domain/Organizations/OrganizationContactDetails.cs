namespace ReestrParse.Domain.Organizations;

public sealed record OrganizationContactDetails(
    string OrganizationId,
    string Name,
    string Inn,
    string Kpp,
    IReadOnlyList<string> Phones,
    string Email,
    string Website,
    string ResponsibleFullName,
    string ResponsiblePosition,
    string ResponsiblePhone,
    string ResponsibleEmail,
    string ManagerFullName,
    string PostalAddress,
    string LocationAddress,
    string DetailUrl,
    string TemplateUrl);
