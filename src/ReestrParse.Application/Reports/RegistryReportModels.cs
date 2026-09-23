namespace ReestrParse.Application.Reports;

public enum RegistryReportMode
{
    SeparateRegionFiles = 0,
    SingleWorkbookByRegion = 1
}

public sealed record RegistryReportRow(
    string Name,
    string Inn,
    string Email,
    bool IsSuccessful = true);

public sealed record RegistryReportSheet(
    string RegionName,
    IReadOnlyList<RegistryReportRow> Rows);

public sealed record RegistryReportSettings(
    RegistryReportMode Mode,
    bool IncludeWithoutEmail,
    bool IncludeFailedOrganizations,
    bool AutoSaveAfterRegion);
