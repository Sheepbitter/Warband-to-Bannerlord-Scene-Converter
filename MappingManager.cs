using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace WarbandToBannerlordConverter;

// Updates asset_mappings.xml, which is responsible for, as the name suggests, the mapping of Warband entities to Bannerlord based on however the user sets them in their GUI.
// The xml is persistent so that as one converts more maps, more assets are already done. Eventually little to no work will be needed when converting Warband scenes to Bannerlord.
public class MissionObject
{
    public string str { get; set; } = "unknown_prop";
    public List<double> pos { get; set; } = new List<double> { 0, 0, 0 };
    public List<double> scale { get; set; } = new List<double> { 1, 1, 1 };
    public List<List<double>> rotation_matrix { get; set; }
}

public class AssetMapping
{
    public string WB { get; set; } = "";
    public string BL { get; set; } = "editor_cube";
    public double OffX { get; set; } = 0; public double OffY { get; set; } = 0; public double OffZ { get; set; } = 0;
    public double RotX { get; set; } = 0; public double RotY { get; set; } = 0; public double RotZ { get; set; } = 0;
    public double ScX { get; set; } = 1; public double ScY { get; set; } = 1; public double ScZ { get; set; } = 1;
    public bool UseOrigin { get; set; } = false;
    public double OriginX { get; set; } = 0; public double OriginY { get; set; } = 0; public double OriginZ { get; set; } = 0;
}

public class MappingManager
{
    public Dictionary<string, AssetMapping> Mappings = new Dictionary<string, AssetMapping>();
    private string _path;
    private bool _useComma;

    public MappingManager(string path, bool useComma = false)
    {
        _path = path;
        _useComma = useComma;
        if (File.Exists(_path)) Load();
    }

    public void EnsureDecimalSeparator(bool useComma)
    {
        _useComma = useComma;
        if (File.Exists(_path) && UsesCommaInXml() != useComma) Save();
    }

    private bool UsesCommaInXml()
    {
        try
        {
            XDocument doc = XDocument.Load(_path);
            return doc.Root.Elements("map").Any(m =>
                new[] { "pos", "rot", "scale", "origin" }
                    .Select(sub => m.Element(sub)?.Attributes() ?? Enumerable.Empty<XAttribute>())
                    .SelectMany(a => a)
                    .Any(a => a.Value.Contains(',')));
        }
        catch { return false; }
    }

    public void Load()
    {
        try
        {
            XDocument doc = XDocument.Load(_path);
            Mappings = doc.Root.Elements("map").ToDictionary(
                x => x.Attribute("wb").Value,
                x => new AssetMapping
                {
                    WB = x.Attribute("wb").Value,
                    BL = x.Attribute("bl").Value,
                    OffX = GetVal(x, "pos", "x"),
                    OffY = GetVal(x, "pos", "y"),
                    OffZ = GetVal(x, "pos", "z"),
                    RotX = GetVal(x, "rot", "x"),
                    RotY = GetVal(x, "rot", "y"),
                    RotZ = GetVal(x, "rot", "z"),
                    ScX = GetVal(x, "scale", "x", 1.0),
                    ScY = GetVal(x, "scale", "y", 1.0),
                    ScZ = GetVal(x, "scale", "z", 1.0),
                    UseOrigin = (bool?)x.Element("origin")?.Attribute("enabled") ?? false,
                    OriginX = GetVal(x, "origin", "x"),
                    OriginY = GetVal(x, "origin", "y"),
                    OriginZ = GetVal(x, "origin", "z")
                });
        }
        catch { }
    }

    private double GetVal(XElement el, string sub, string attr, double def = 0)
    {
        string raw = el.Element(sub)?.Attribute(attr)?.Value;
        if (raw == null) return def;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double dot) &&
            !raw.Contains(','))
            return dot;
        return double.TryParse(raw, NumberStyles.Float, CommaCulture, out double comma) ? comma : def;
    }

    private static readonly CultureInfo CommaCulture = BuildCommaCulture();

    private static CultureInfo BuildCommaCulture()
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        culture.NumberFormat.NumberGroupSeparator = ".";
        return culture;
    }

    private string FormatVal(double v) =>
        v.ToString("R", _useComma ? CommaCulture : CultureInfo.InvariantCulture);

    public void Save()
    {
        var root = new XElement("mappings",
            Mappings.Values.Select(m => new XElement("map",
                new XAttribute("wb", m.WB), new XAttribute("bl", m.BL),
                new XElement("pos", new XAttribute("x", FormatVal(m.OffX)), new XAttribute("y", FormatVal(m.OffY)), new XAttribute("z", FormatVal(m.OffZ))),
                new XElement("rot", new XAttribute("x", FormatVal(m.RotX)), new XAttribute("y", FormatVal(m.RotY)), new XAttribute("z", FormatVal(m.RotZ))),
                new XElement("scale", new XAttribute("x", FormatVal(m.ScX)), new XAttribute("y", FormatVal(m.ScY)), new XAttribute("z", FormatVal(m.ScZ))),
                new XElement("origin",
                    new XAttribute("enabled", m.UseOrigin),
                    new XAttribute("x", FormatVal(m.OriginX)),
                    new XAttribute("y", FormatVal(m.OriginY)),
                    new XAttribute("z", FormatVal(m.OriginZ)))
            ))
        );
        new XDocument(root).Save(_path);
    }
}