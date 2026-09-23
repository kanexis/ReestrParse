using System.IO.Compression;
using System.Text;
using System.Xml;

namespace ReestrParse.Application.Reports;

/// <summary>
/// Lightweight XLSX writer with no external Office dependency.
/// The public report intentionally contains only: Наименование, ИНН, Email.
/// </summary>
public sealed class XlsxRegistryReportWriter : IRegistryReportWriter
{
    private const string SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public Task<string> WriteRegionAsync(
        string outputDirectory,
        RegistryReportSheet sheet,
        RegistryReportSettings settings,
        DateOnly reportDate,
        CancellationToken cancellationToken = default)
    {
        var path = BuildUniquePath(
            outputDirectory,
            $"Реестр_{SanitizeFilePart(sheet.RegionName)}_Теплоснабжение_{reportDate:yyyy-MM-dd}.xlsx");

        return WriteAsync(path, [sheet], settings, cancellationToken);
    }

    public Task<string> WriteGlobalAsync(
        string outputDirectory,
        IReadOnlyList<RegistryReportSheet> sheets,
        RegistryReportSettings settings,
        DateOnly reportDate,
        CancellationToken cancellationToken = default,
        string? existingPath = null)
    {
        var path = string.IsNullOrWhiteSpace(existingPath)
            ? BuildUniquePath(
                outputDirectory,
                $"Реестр_Теплоснабжение_{reportDate:yyyy-MM-dd}.xlsx")
            : existingPath;

        return WriteAsync(path, sheets, settings, cancellationToken);
    }

    private static async Task<string> WriteAsync(
        string path,
        IReadOnlyList<RegistryReportSheet> sheets,
        RegistryReportSettings settings,
        CancellationToken cancellationToken)
    {
        if (sheets.Count == 0)
            throw new InvalidOperationException("В отчёте нет ни одного региона.");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: false);

            var prepared = PrepareSheets(sheets, settings);
            WriteContentTypes(archive, prepared.Count);
            WriteRootRelationships(archive);
            WriteCoreProperties(archive);
            WriteAppProperties(archive, prepared);
            WriteWorkbook(archive, prepared);
            WriteWorkbookRelationships(archive, prepared.Count);
            WriteStyles(archive);

            for (var i = 0; i < prepared.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteWorksheet(archive, i + 1, prepared[i].Rows);
            }
        }, cancellationToken);

        return path;
    }

    private static IReadOnlyList<PreparedSheet> PrepareSheets(
        IReadOnlyList<RegistryReportSheet> sheets,
        RegistryReportSettings settings)
    {
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prepared = new List<PreparedSheet>(sheets.Count);

        foreach (var sheet in sheets)
        {
            var rows = sheet.Rows
                .Where(x => settings.IncludeFailedOrganizations || x.IsSuccessful)
                .Where(x => settings.IncludeWithoutEmail || !string.IsNullOrWhiteSpace(x.Email))
                .GroupBy(x => $"{x.Inn}|{x.Name}", StringComparer.OrdinalIgnoreCase)
                .Select(g => g
                    .OrderByDescending(x => !string.IsNullOrWhiteSpace(x.Email))
                    .First())
                .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(x => x.Inn, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var name = CreateUniqueSheetName(sheet.RegionName, usedNames);
            prepared.Add(new PreparedSheet(name, rows));
        }

        return prepared;
    }

    private static void WriteContentTypes(ZipArchive archive, int sheetCount)
    {
        var entry = archive.CreateEntry("[Content_Types].xml", CompressionLevel.Fastest);
        using var writer = CreateWriter(entry);
        writer.WriteStartDocument(true);
        writer.WriteStartElement("Types", "http://schemas.openxmlformats.org/package/2006/content-types");
        WriteEmpty(writer, "Default", ("Extension", "rels"), ("ContentType", "application/vnd.openxmlformats-package.relationships+xml"));
        WriteEmpty(writer, "Default", ("Extension", "xml"), ("ContentType", "application/xml"));
        WriteEmpty(writer, "Override", ("PartName", "/xl/workbook.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"));
        WriteEmpty(writer, "Override", ("PartName", "/xl/styles.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"));
        WriteEmpty(writer, "Override", ("PartName", "/docProps/core.xml"), ("ContentType", "application/vnd.openxmlformats-package.core-properties+xml"));
        WriteEmpty(writer, "Override", ("PartName", "/docProps/app.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.extended-properties+xml"));
        for (var i = 1; i <= sheetCount; i++)
            WriteEmpty(writer, "Override", ("PartName", $"/xl/worksheets/sheet{i}.xml"), ("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"));
        writer.WriteEndElement();
    }

    private static void WriteRootRelationships(ZipArchive archive)
    {
        var entry = archive.CreateEntry("_rels/.rels", CompressionLevel.Fastest);
        using var writer = CreateWriter(entry);
        writer.WriteStartDocument(true);
        writer.WriteStartElement("Relationships", PackageRelationshipNs);
        WriteEmpty(writer, "Relationship",
            ("Id", "rId1"),
            ("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"),
            ("Target", "xl/workbook.xml"));
        WriteEmpty(writer, "Relationship",
            ("Id", "rId2"),
            ("Type", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"),
            ("Target", "docProps/core.xml"));
        WriteEmpty(writer, "Relationship",
            ("Id", "rId3"),
            ("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties"),
            ("Target", "docProps/app.xml"));
        writer.WriteEndElement();
    }

    private static void WriteCoreProperties(ZipArchive archive)
    {
        var entry = archive.CreateEntry("docProps/core.xml", CompressionLevel.Fastest);
        using var writer = CreateWriter(entry);
        writer.WriteStartDocument(true);
        writer.WriteStartElement("cp", "coreProperties", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties");
        writer.WriteAttributeString("xmlns", "dc", null, "http://purl.org/dc/elements/1.1/");
        writer.WriteAttributeString("xmlns", "dcterms", null, "http://purl.org/dc/terms/");
        writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
        writer.WriteElementString("dc", "creator", "http://purl.org/dc/elements/1.1/", "ReestrParse");
        writer.WriteElementString("cp", "lastModifiedBy", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties", "ReestrParse");
        writer.WriteStartElement("dcterms", "created", "http://purl.org/dc/terms/");
        writer.WriteAttributeString("xsi", "type", "http://www.w3.org/2001/XMLSchema-instance", "dcterms:W3CDTF");
        writer.WriteString(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteAppProperties(ZipArchive archive, IReadOnlyList<PreparedSheet> sheets)
    {
        var entry = archive.CreateEntry("docProps/app.xml", CompressionLevel.Fastest);
        using var writer = CreateWriter(entry);
        writer.WriteStartDocument(true);
        writer.WriteStartElement("Properties", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties");
        writer.WriteAttributeString("xmlns", "vt", null, "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes");
        writer.WriteElementString("Application", "ReestrParse");
        writer.WriteElementString("AppVersion", "0.9.7");
        writer.WriteStartElement("TitlesOfParts");
        writer.WriteStartElement("vt", "vector", "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes");
        writer.WriteAttributeString("size", sheets.Count.ToString());
        writer.WriteAttributeString("baseType", "lpstr");
        foreach (var sheet in sheets)
            writer.WriteElementString("vt", "lpstr", "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes", sheet.Name);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteWorkbook(ZipArchive archive, IReadOnlyList<PreparedSheet> sheets)
    {
        var entry = archive.CreateEntry("xl/workbook.xml", CompressionLevel.Fastest);
        using var writer = CreateWriter(entry);
        writer.WriteStartDocument(true);
        writer.WriteStartElement("workbook", SpreadsheetNs);
        writer.WriteAttributeString("xmlns", "r", null, RelationshipNs);
        writer.WriteStartElement("sheets");
        for (var i = 0; i < sheets.Count; i++)
        {
            writer.WriteStartElement("sheet");
            writer.WriteAttributeString("name", sheets[i].Name);
            writer.WriteAttributeString("sheetId", (i + 1).ToString());
            writer.WriteAttributeString("r", "id", RelationshipNs, $"rId{i + 1}");
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteWorkbookRelationships(ZipArchive archive, int sheetCount)
    {
        var entry = archive.CreateEntry("xl/_rels/workbook.xml.rels", CompressionLevel.Fastest);
        using var writer = CreateWriter(entry);
        writer.WriteStartDocument(true);
        writer.WriteStartElement("Relationships", PackageRelationshipNs);
        for (var i = 1; i <= sheetCount; i++)
        {
            WriteEmpty(writer, "Relationship",
                ("Id", $"rId{i}"),
                ("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"),
                ("Target", $"worksheets/sheet{i}.xml"));
        }
        WriteEmpty(writer, "Relationship",
            ("Id", $"rId{sheetCount + 1}"),
            ("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"),
            ("Target", "styles.xml"));
        writer.WriteEndElement();
    }

    private static void WriteStyles(ZipArchive archive)
    {
        var entry = archive.CreateEntry("xl/styles.xml", CompressionLevel.Fastest);
        using var writer = CreateWriter(entry);
        writer.WriteStartDocument(true);
        writer.WriteStartElement("styleSheet", SpreadsheetNs);

        writer.WriteStartElement("fonts"); writer.WriteAttributeString("count", "2");
        writer.WriteStartElement("font");
        WriteEmpty(writer, "sz", ("val", "11"));
        WriteEmpty(writer, "name", ("val", "Segoe UI"));
        writer.WriteEndElement();
        writer.WriteStartElement("font");
        WriteEmpty(writer, "b");
        WriteEmpty(writer, "color", ("rgb", "FFFFFFFF"));
        WriteEmpty(writer, "sz", ("val", "11"));
        WriteEmpty(writer, "name", ("val", "Segoe UI"));
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("fills"); writer.WriteAttributeString("count", "3");
        writer.WriteStartElement("fill"); writer.WriteStartElement("patternFill"); writer.WriteAttributeString("patternType", "none"); writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteStartElement("fill"); writer.WriteStartElement("patternFill"); writer.WriteAttributeString("patternType", "gray125"); writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteStartElement("fill"); writer.WriteStartElement("patternFill"); writer.WriteAttributeString("patternType", "solid"); writer.WriteStartElement("fgColor"); writer.WriteAttributeString("rgb", "FF1D4ED8"); writer.WriteEndElement(); writer.WriteStartElement("bgColor"); writer.WriteAttributeString("indexed", "64"); writer.WriteEndElement(); writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("borders"); writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("border"); writer.WriteElementString("left", string.Empty); writer.WriteElementString("right", string.Empty); writer.WriteElementString("top", string.Empty); writer.WriteElementString("bottom", string.Empty); writer.WriteElementString("diagonal", string.Empty); writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("cellStyleXfs"); writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("xf"); writer.WriteAttributeString("numFmtId", "0"); writer.WriteAttributeString("fontId", "0"); writer.WriteAttributeString("fillId", "0"); writer.WriteAttributeString("borderId", "0"); writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("cellXfs"); writer.WriteAttributeString("count", "2");
        writer.WriteStartElement("xf"); writer.WriteAttributeString("numFmtId", "0"); writer.WriteAttributeString("fontId", "0"); writer.WriteAttributeString("fillId", "0"); writer.WriteAttributeString("borderId", "0"); writer.WriteAttributeString("xfId", "0"); writer.WriteEndElement();
        writer.WriteStartElement("xf"); writer.WriteAttributeString("numFmtId", "0"); writer.WriteAttributeString("fontId", "1"); writer.WriteAttributeString("fillId", "2"); writer.WriteAttributeString("borderId", "0"); writer.WriteAttributeString("xfId", "0"); writer.WriteAttributeString("applyFont", "1"); writer.WriteAttributeString("applyFill", "1"); writer.WriteAttributeString("applyAlignment", "1"); writer.WriteStartElement("alignment"); writer.WriteAttributeString("horizontal", "center"); writer.WriteAttributeString("vertical", "center"); writer.WriteEndElement(); writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("cellStyles"); writer.WriteAttributeString("count", "1");
        writer.WriteStartElement("cellStyle"); writer.WriteAttributeString("name", "Normal"); writer.WriteAttributeString("xfId", "0"); writer.WriteAttributeString("builtinId", "0"); writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteWorksheet(ZipArchive archive, int index, IReadOnlyList<RegistryReportRow> rows)
    {
        var entry = archive.CreateEntry($"xl/worksheets/sheet{index}.xml", CompressionLevel.Fastest);
        using var writer = CreateWriter(entry);
        writer.WriteStartDocument(true);
        writer.WriteStartElement("worksheet", SpreadsheetNs);

        var lastRow = Math.Max(1, rows.Count + 1);
        writer.WriteStartElement("dimension"); writer.WriteAttributeString("ref", $"A1:C{lastRow}"); writer.WriteEndElement();

        writer.WriteStartElement("sheetViews");
        writer.WriteStartElement("sheetView"); writer.WriteAttributeString("workbookViewId", "0");
        writer.WriteStartElement("pane"); writer.WriteAttributeString("ySplit", "1"); writer.WriteAttributeString("topLeftCell", "A2"); writer.WriteAttributeString("activePane", "bottomLeft"); writer.WriteAttributeString("state", "frozen"); writer.WriteEndElement();
        writer.WriteEndElement(); writer.WriteEndElement();

        writer.WriteStartElement("sheetFormatPr"); writer.WriteAttributeString("defaultRowHeight", "18"); writer.WriteEndElement();

        writer.WriteStartElement("cols");
        WriteColumn(writer, 1, 1, 58); WriteColumn(writer, 2, 2, 18); WriteColumn(writer, 3, 3, 42);
        writer.WriteEndElement();

        writer.WriteStartElement("sheetData");
        writer.WriteStartElement("row"); writer.WriteAttributeString("r", "1"); writer.WriteAttributeString("ht", "24"); writer.WriteAttributeString("customHeight", "1");
        WriteInlineCell(writer, "A1", "Наименование", style: 1);
        WriteInlineCell(writer, "B1", "ИНН", style: 1);
        WriteInlineCell(writer, "C1", "Email", style: 1);
        writer.WriteEndElement();

        for (var i = 0; i < rows.Count; i++)
        {
            var rowNumber = i + 2;
            var row = rows[i];
            writer.WriteStartElement("row"); writer.WriteAttributeString("r", rowNumber.ToString());
            WriteInlineCell(writer, $"A{rowNumber}", row.Name);
            WriteInlineCell(writer, $"B{rowNumber}", row.Inn);
            WriteInlineCell(writer, $"C{rowNumber}", row.Email);
            writer.WriteEndElement();
        }
        writer.WriteEndElement();

        writer.WriteStartElement("autoFilter"); writer.WriteAttributeString("ref", $"A1:C{lastRow}"); writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteInlineCell(XmlWriter writer, string cellReference, string value, int style = 0)
    {
        writer.WriteStartElement("c");
        writer.WriteAttributeString("r", cellReference);
        writer.WriteAttributeString("t", "inlineStr");
        if (style != 0)
            writer.WriteAttributeString("s", style.ToString());
        writer.WriteStartElement("is");
        writer.WriteStartElement("t");
        if (!string.IsNullOrEmpty(value) && (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1])))
            writer.WriteAttributeString("xml", "space", null, "preserve");
        writer.WriteString(value ?? string.Empty);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteColumn(XmlWriter writer, int min, int max, double width)
    {
        writer.WriteStartElement("col");
        writer.WriteAttributeString("min", min.ToString());
        writer.WriteAttributeString("max", max.ToString());
        writer.WriteAttributeString("width", width.ToString(System.Globalization.CultureInfo.InvariantCulture));
        writer.WriteAttributeString("customWidth", "1");
        writer.WriteEndElement();
    }

    private static XmlWriter CreateWriter(ZipArchiveEntry entry)
        => XmlWriter.Create(entry.Open(), new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = false,
            CloseOutput = true
        });

    private static void WriteEmpty(XmlWriter writer, string elementName, params (string Name, string Value)[] attributes)
    {
        writer.WriteStartElement(elementName);
        foreach (var (name, value) in attributes)
            writer.WriteAttributeString(name, value);
        writer.WriteEndElement();
    }

    private static string BuildUniquePath(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Не выбрана папка для Excel-отчёта.");

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
            return path;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var i = 2; i < 10_000; i++)
        {
            var candidate = Path.Combine(directory, $"{stem}_{i}{extension}");
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new IOException("Не удалось подобрать свободное имя файла отчёта.");
    }

    private static string CreateUniqueSheetName(string rawName, ISet<string> used)
    {
        var invalid = new HashSet<char> { '[', ']', ':', '*', '?', '/', '\\' };
        var cleaned = new string((rawName ?? string.Empty)
            .Where(c => !invalid.Contains(c) && !char.IsControl(c))
            .ToArray()).Trim().Trim('\'');
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "Регион";
        if (cleaned.Length > 31)
            cleaned = cleaned[..31];

        var candidate = cleaned;
        var suffix = 2;
        while (!used.Add(candidate))
        {
            var suffixText = $" ({suffix++})";
            var maxBase = Math.Max(1, 31 - suffixText.Length);
            candidate = cleaned[..Math.Min(cleaned.Length, maxBase)] + suffixText;
        }

        return candidate;
    }

    private static string SanitizeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string((value ?? string.Empty)
            .Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c)
            .ToArray());
        cleaned = string.Join(" ", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.', '_');
        return string.IsNullOrWhiteSpace(cleaned) ? "Регион" : cleaned;
    }

    private sealed record PreparedSheet(string Name, IReadOnlyList<RegistryReportRow> Rows);
}
