using ReestrParse.Domain.Organizations;
using Xunit;

namespace ReestrParse.Application.Tests;

public sealed class OrganizationContactDetailsTests
{
    [Theory]
    [InlineData("manager@example.ru", "responsible@example.ru", "org@example.ru", "manager@example.ru")]
    [InlineData("", "responsible@example.ru", "org@example.ru", "org@example.ru")]
    [InlineData("", "responsible@example.ru", "", "responsible@example.ru")]
    [InlineData("", "", "", "")]
    public void PreferredEmail_UsesExpectedPriority(
        string managerEmail,
        string responsibleEmail,
        string organizationEmail,
        string expected)
    {
        var details = new OrganizationContactDetails(
            OrganizationId: "1",
            Name: "Test",
            Inn: "2200000000",
            Kpp: "220001001",
            Phones: [],
            Email: organizationEmail,
            Website: "",
            ResponsibleFullName: "",
            ResponsiblePosition: "",
            ResponsiblePhone: "",
            ResponsibleEmail: responsibleEmail,
            ManagerFullName: "",
            PostalAddress: "",
            LocationAddress: "",
            DetailUrl: "",
            TemplateUrl: "",
            ManagerEmail: managerEmail);

        Assert.Equal(expected, details.PreferredEmail);
    }
    [Fact]
    public void HasOrganizationContacts_RecognizesManagerEmailOnly()
    {
        var details = new OrganizationContactDetails(
            OrganizationId: "1",
            Name: "Test",
            Inn: "2200000000",
            Kpp: "220001001",
            Phones: [],
            Email: "",
            Website: "",
            ResponsibleFullName: "",
            ResponsiblePosition: "",
            ResponsiblePhone: "",
            ResponsibleEmail: "",
            ManagerFullName: "",
            PostalAddress: "",
            LocationAddress: "",
            DetailUrl: "",
            TemplateUrl: "",
            ManagerEmail: "manager@example.ru");

        Assert.True(details.HasOrganizationContacts);
    }

}
