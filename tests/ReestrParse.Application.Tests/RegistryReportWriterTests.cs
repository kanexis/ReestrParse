using System.IO.Compression;
using ReestrParse.Application.Reports;
using Xunit;

namespace ReestrParse.Application.Tests;

public sealed class RegistryReportWriterTests
{
    [Fact]
    public async Task RegionReport_UsesExpectedNameAndOnlyThreeColumns()
    {
        var directory = Path.Combine(Path.GetTempPath(), "reestrparse-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var writer = new XlsxRegistryReportWriter();
            var settings = new RegistryReportSettings(
                RegistryReportMode.SeparateRegionFiles,
                IncludeWithoutEmail: true,
                IncludeFailedOrganizations: false,
                AutoSaveAfterRegion: true);

            var path = await writer.WriteRegionAsync(
                directory,
                new RegistryReportSheet("Алтайский край",
                [
                    new RegistryReportRow("ООО Тест", "2200000000", "boss@example.ru", true),
                    new RegistryReportRow("ООО Ошибка", "2200000001", "", false)
                ]),
                settings,
                new DateOnly(2026, 9, 22));

            Assert.Equal("Реестр_Алтайский край_Теплоснабжение_2026-09-22.xlsx", Path.GetFileName(path));
            Assert.True(File.Exists(path));

            using var archive = ZipFile.OpenRead(path);
            var sheet = archive.GetEntry("xl/worksheets/sheet1.xml");
            Assert.NotNull(sheet);
            using var reader = new StreamReader(sheet!.Open());
            var xml = await reader.ReadToEndAsync();

            Assert.Contains("Наименование", xml);
            Assert.Contains("ИНН", xml);
            Assert.Contains("Email", xml);
            Assert.Contains("boss@example.ru", xml);
            Assert.DoesNotContain("ООО Ошибка", xml);
            Assert.DoesNotContain("D1", xml);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
