// Seeds VistumblerMAUI's translations from the original Vistumbler's language files (VistumblerMDB/Languages/*.ini),
// which users contributed over the years. Where one of our English strings matches one of the original's (ignoring
// '&' shortcut markers, case, a trailing ':' and ellipses), the original's translation is copied into
// Resources/Strings/AppResources.<code>.resx. Existing translations are kept, so it can be re-run as more of the app
// moves into AppResources.resx; strings it can't match stay English until someone translates them.
//
//   dotnet run build/tools/ImportVistumblerLanguages.cs -- <path to VistumblerMDB/Languages> [path to Resources/Strings]
#:property TargetFramework=net10.0

using System.Globalization;
using System.Text;
using System.Xml.Linq;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var languages = args.Length > 0 ? args[0] : throw new ArgumentException("Pass the path to VistumblerMDB/Languages");
var strings = args.Length > 1 ? args[1] : Path.Combine("VistumblerMAUI", "Resources", "Strings");
var english = Path.Combine(strings, "AppResources.resx");

// File → culture, and the Windows codepage the file was saved in when it isn't UTF-8
var files = new (string File, string Culture, int CodePage)[]
{
    ("Brazilian_Portuguese.ini", "pt-BR", 1252), ("Bulgarian.ini", "bg", 1251), ("Chinese_Traditional.ini", "zh-Hant", 950),
    ("Czech.ini", "cs", 1250), ("Danish.ini", "da", 1252), ("Deutsch.ini", "de", 1252), ("Dutch.ini", "nl", 1252),
    ("French.ini", "fr", 1252), ("Greek.ini", "el", 1253), ("Italiano.ini", "it", 1252), ("Japanese.ini", "ja", 932),
    ("Norwegian.ini", "nb", 1252), ("Polish.ini", "pl", 1250), ("Russian.ini", "ru", 1251),
    ("Spanish2.ini", "es", 1252), ("Spanish.ini", "es", 1252),   // the fuller Spanish first; the older one fills gaps
    ("Swedish.ini", "sv", 1252), ("Turkish.ini", "tr", 1254),
};

static string Normalize(string s) =>
    s.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&")
     .Replace("…", "").Replace("...", "").Trim().TrimEnd(':').Trim().ToLowerInvariant();

// The original's text sections, key → value (the search words parse netsh output, which this app doesn't use)
static Dictionary<string, string> ReadIni(string path, int codePage)
{
    var bytes = File.ReadAllBytes(path);
    string text;
    try { text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
    catch (DecoderFallbackException) { text = Encoding.GetEncoding(codePage).GetString(bytes); }

    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    string section = "";
    foreach (var raw in text.Split('\n'))
    {
        var line = raw.Trim().TrimStart('﻿');
        if (line.StartsWith('[')) { section = line.Trim('[', ']'); continue; }
        if (section is not ("GuiText" or "Column_Names")) continue;
        int eq = line.IndexOf('=');
        if (eq <= 0) continue;
        var value = line[(eq + 1)..].Trim();
        if (value.Length > 0) values[line[..eq].Trim()] = value;
    }
    return values;
}

// Our English strings, by normalized text; text with placeholders is left for translators
var ours = XDocument.Load(english).Root!.Elements("data")
    .Select(d => (Key: (string)d.Attribute("name")!, Value: (string?)d.Element("value") ?? ""))
    .Where(d => !d.Value.Contains('{'))
    .ToList();
var originalEnglish = ReadIni(Path.Combine(languages, "English.ini"), 1252);

foreach (var (file, culture, codePage) in files)
{
    var path = Path.Combine(languages, file);
    if (!File.Exists(path)) { Console.WriteLine($"{file}: not found"); continue; }
    var translated = ReadIni(path, codePage);

    var target = Path.Combine(strings, $"AppResources.{culture}.resx");
    var doc = File.Exists(target) ? XDocument.Load(target) : XDocument.Load(english);
    if (!File.Exists(target))
    {
        doc.Root!.Elements("data").Remove();
        doc.Root.Nodes().OfType<XComment>().Remove();
        doc.Root.AddFirst(new XComment($" {new CultureInfo(culture).EnglishName} text for VistumblerMAUI. Strings not here show in English (AppResources.resx). " +
                                       "Seeded from the original Vistumbler's user-contributed language files by build/tools/ImportVistumblerLanguages.cs. "));
    }
    // A machine-translated draft (comment "Draft…", build/tools/ApplyDraftTranslations.cs) gives way to a person's
    // translation from the original's files; anything else already there is kept
    static bool IsDraft(XElement d) => ((string?)d.Element("comment"))?.StartsWith("Draft", StringComparison.Ordinal) == true;
    var existing = doc.Root!.Elements("data").Where(d => !IsDraft(d)).Select(d => (string)d.Attribute("name")!).ToHashSet();

    int added = 0;
    foreach (var (key, value) in ours)
    {
        if (existing.Contains(key)) continue;
        var match = originalEnglish.FirstOrDefault(o => Normalize(o.Value) == Normalize(value));
        if (match.Key is null || !translated.TryGetValue(match.Key, out var translation)) continue;
        if (Normalize(translation) == Normalize(match.Value) && culture != "en") continue;   // never translated, still English
        doc.Root.Elements("data").Where(d => (string)d.Attribute("name")! == key).Remove();   // the draft it replaces

        var text = translation.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&").Trim();
        if (value.EndsWith('…') && !text.EndsWith('…')) text = text.TrimEnd('.', ':', ' ') + "…";
        if (!value.EndsWith(':')) text = text.TrimEnd(':').TrimEnd();

        doc.Root.Add(new XElement("data",
            new XAttribute("name", key),
            new XAttribute(XNamespace.Xml + "space", "preserve"),
            new XElement("value", text),
            new XElement("comment", $"Vistumbler {file} {match.Key}")));
        existing.Add(key);
        added++;
    }
    if (added == 0 && !File.Exists(target)) { Console.WriteLine($"{file} → {culture}: nothing matched"); continue; }
    doc.Save(target);
    Console.WriteLine($"{file} → {culture}: {added} added, {existing.Count} in all");
}
