namespace ReestrParse.Application.Reports;

public interface IRegistryReportWriter
{
    Task<string> WriteRegionAsync(
        string outputDirectory,
        RegistryReportSheet sheet,
        RegistryReportSettings settings,
        DateOnly reportDate,
        CancellationToken cancellationToken = default);

    Task<string> WriteGlobalAsync(
        string outputDirectory,
        IReadOnlyList<RegistryReportSheet> sheets,
        RegistryReportSettings settings,
        DateOnly reportDate,
        CancellationToken cancellationToken = default,
        string? existingPath = null);
}
