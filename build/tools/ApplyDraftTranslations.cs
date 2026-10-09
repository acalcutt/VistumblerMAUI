// Adds machine-translated drafts to Resources/Strings/AppResources.<code>.resx for the strings a language doesn't
// have yet. Input: one file per language, <code>.txt, with lines "Key=Translation" (\n for a line break). Each draft
// is marked with the comment "Draft (machine translation)…", so a translator can find and review them, and
// build/tools/ImportVistumblerLanguages.cs replaces one whenever the original Vistumbler had a person's translation.
// Translations already in a file, drafts included, are never overwritten.
//
//   dotnet run --file build/tools/ApplyDraftTranslations.cs -- <folder of <code>.txt files> [path to Resources/Strings]
#:property TargetFramework=net10.0

using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var drafts = args[0];
var strings = args.Length > 1 ? args[1] : Path.Combine("VistumblerMAUI", "Resources", "Strings");
var english = XDocument.Load(Path.Combine(strings, "AppResources.resx")).Root!.Elements("data")
    .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

static int Placeholders(string s) => Regex.Matches(s, @"\{\d+\}").Select(m => m.Value).Distinct().Count();

foreach (var file in Directory.GetFiles(drafts, "*.txt").OrderBy(f => f))
{
    var culture = Path.GetFileNameWithoutExtension(file);
    var target = Path.Combine(strings, $"AppResources.{culture}.resx");
    XDocument doc;
    if (File.Exists(target)) doc = XDocument.Load(target);
    else
    {
        doc = XDocument.Load(Path.Combine(strings, "AppResources.resx"));
        doc.Root!.Elements("data").Remove();
        doc.Root.Nodes().OfType<XComment>().Remove();
        doc.Root.AddFirst(new XComment($" {new CultureInfo(culture).EnglishName} text for VistumblerMAUI. Strings not here show in English (AppResources.resx). "));
    }
    var have = doc.Root!.Elements("data").Select(d => (string)d.Attribute("name")!).ToHashSet();

    int added = 0, skipped = 0;
    foreach (var line in File.ReadLines(file))
    {
        int eq = line.IndexOf('=');
        if (eq <= 0 || line.StartsWith('#')) continue;
        var key = line[..eq].Trim();
        var text = line[(eq + 1)..].Trim().Replace("\\n", "\n");
        if (!english.TryGetValue(key, out var source) || have.Contains(key) || text.Length == 0) { skipped++; continue; }
        // A draft that lost or gained a {0} would show the wrong thing; leave that string in English
        if (Placeholders(text) != Placeholders(source)) { Console.WriteLine($"  {culture} {key}: placeholders differ, skipped"); skipped++; continue; }
        doc.Root.Add(new XElement("data",
            new XAttribute("name", key),
            new XAttribute(XNamespace.Xml + "space", "preserve"),
            new XElement("value", text),
            new XElement("comment", "Draft (machine translation), please review")));
        have.Add(key);
        added++;
    }
    doc.Save(target);
    Console.WriteLine($"{culture}: {added} drafts added, {skipped} skipped, {have.Count} of {english.Count} in all");
}
