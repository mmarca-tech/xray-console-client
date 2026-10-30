using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;

namespace XrayConsoleClient;

static class ReportFiles
{
    // The report file itself, or the files of the report folder whose name matches, sorted by name.
    public static List<string> Resolve(string path, Func<string, bool> fileNameFilter)
    {
        if (Directory.Exists(path))
        {
            return Directory.GetFiles(path)
                .Where(file => fileNameFilter(Path.GetFileName(file)))
                .Order(StringComparer.Ordinal)
                .ToList();
        }

        if (File.Exists(path))
        {
            return [path];
        }

        throw new XrayInputException($"Results not found: {path}");
    }

    // Allure results reference their attachments relative to the results folder.
    public static string RequireDirectory(string path) =>
        Directory.Exists(path) ? path : throw new XrayInputException($"{path} must be the results folder");

    public static string RequireFile(string path) =>
        File.Exists(path) ? path : throw new XrayInputException($"Results file not found: {path}");

    public static JsonNode? ReadJson(string file)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(file));
        }
        catch (JsonException ex)
        {
            throw new XrayInputException($"{file} is not valid json: {ex.Message}");
        }
    }

    public static XElement ReadXml(string file)
    {
        try
        {
            using var reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            return XDocument.Load(reader).Root ?? throw new XrayInputException($"{file} is an empty xml document");
        }
        catch (XmlException ex)
        {
            throw new XrayInputException($"{file} is not valid xml: {ex.Message}");
        }
    }

    // Child elements by local name, so prefixed roots like ns2:test-suite read the same as unprefixed ones.
    public static IEnumerable<XElement> Children(XElement? element, string localName) =>
        element?.Elements().Where(child => child.Name.LocalName == localName) ?? [];

    public static XElement? Child(XElement? element, string localName) => Children(element, localName).FirstOrDefault();

    public static string Base64(string file) => Convert.ToBase64String(File.ReadAllBytes(file));
}
