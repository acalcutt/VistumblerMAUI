// Moves the English text in XAML pages into Resources/Strings/AppResources.resx: Text, Title, Placeholder and
// PickerTitle attributes, Setter values for Text, and bindings' StringFormat='…' become {loc:T Key}. A text already
// in the file reuses its key; otherwise the key is made from the page and the text's first words. Comments, bindings
// and text without letters (✕, ⇪) are left alone.
//
//   dotnet run build/tools/LocalizeXaml.cs -- <resx> <page.xaml> [more.xaml…]
#:property TargetFramework=net10.0

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var resxPath = args[0];
var resx = XDocument.Load(resxPath, LoadOptions.PreserveWhitespace);
var root = resx.Root!;
var byValue = new Dictionary<string, string>(StringComparer.Ordinal);
var keys = new HashSet<string>(StringComparer.Ordinal);
foreach (var d in root.Elements("data"))
{
    var k = (string)d.Attribute("name")!;
    keys.Add(k);
    byValue.TryAdd((string?)d.Element("value") ?? "", k);
}

var attr = new Regex(@"\b(Text|Title|Placeholder|PickerTitle)=""([^""{][^""]*)""");
var setter = new Regex(@"(<Setter\s+Property=""Text""\s+Value="")([^""{][^""]*)("")");
var format = new Regex(@"StringFormat='([^']*)'");
var comment = new Regex(@"<!--.*?-->", RegexOptions.Singleline);

foreach (var page in args.Skip(1))
{
    var text = File.ReadAllText(page);
    var prefix = Path.GetFileNameWithoutExtension(page).Replace("Page", "").Replace("ScanControlBar", "Bar");
    var added = new List<(string Key, string Value)>();

    string Key(string value)
    {
        if (byValue.TryGetValue(value, out var existing)) return existing;
        var words = Regex.Matches(value, @"[\p{L}\p{N}]+").Select(m => char.ToUpperInvariant(m.Value[0]) + m.Value[1..]).Take(5);
        var key = prefix + "_" + string.Concat(words);
        if (key.Length > 48) key = key[..48];
        var unique = key;
        for (int n = 2; keys.Contains(unique); n++) unique = key + n;
        keys.Add(unique);
        byValue[value] = unique;
        added.Add((unique, value));
        return unique;
    }

    static bool Translatable(string decoded) => decoded.Any(char.IsLetter) && decoded != "VistumblerMAUI" && !decoded.Contains("://");

    string Convert(string segment)
    {
        segment = attr.Replace(segment, m =>
        {
            var value = WebUtility.HtmlDecode(m.Groups[2].Value);
            return Translatable(value) ? $"{m.Groups[1].Value}=\"{{loc:T {Key(value)}}}\"" : m.Value;
        });
        segment = setter.Replace(segment, m =>
        {
            var value = WebUtility.HtmlDecode(m.Groups[2].Value);
            return Translatable(value) ? $"{m.Groups[1].Value}{{loc:T {Key(value)}}}{m.Groups[3].Value}" : m.Value;
        });
        segment = format.Replace(segment, m =>
        {
            var value = WebUtility.HtmlDecode(m.Groups[1].Value);
            return Translatable(value) ? $"StringFormat={{loc:T {Key(value)}}}" : m.Value;
        });
        return segment;
    }

    // Convert everything outside comments
    var output = new StringBuilder();
    int at = 0;
    foreach (Match c in comment.Matches(text))
    {
        output.Append(Convert(text[at..c.Index])).Append(c.Value);
        at = c.Index + c.Length;
    }
    output.Append(Convert(text[at..]));
    var result = output.ToString();

    if (result.Contains("{loc:T") && !result.Contains("xmlns:loc="))
        result = Regex.Replace(result, @"(xmlns:x=""http://schemas.microsoft.com/winfx/2009/xaml"")(\r?\n)(\s*)",
            m => $"{m.Groups[1].Value}{m.Groups[2].Value}{m.Groups[3].Value}xmlns:loc=\"clr-namespace:VistumblerMAUI.Localization\"{m.Groups[2].Value}{m.Groups[3].Value}", RegexOptions.None, TimeSpan.FromSeconds(5));
    if (result != text) File.WriteAllText(page, result, new UTF8Encoding(false));

    if (added.Count > 0)
    {
        root.Add(new XText("\n\n  "), new XComment($" {Path.GetFileName(page)} "));
        foreach (var (key, value) in added)
            root.Add(new XText("\n  "), new XElement("data", new XAttribute("name", key),
                new XAttribute(XNamespace.Xml + "space", "preserve"), new XElement("value", value)));
    }
    Console.WriteLine($"{Path.GetFileName(page)}: {added.Count} new strings");
}
root.Add(new XText("\n"));
resx.Save(resxPath, SaveOptions.DisableFormatting);
