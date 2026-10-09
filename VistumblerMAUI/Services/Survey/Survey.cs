using System.Text.Json;
using System.Text.Json.Serialization;

namespace VistumblerMAUI.Services.Survey;

/// <summary>
/// An indoor site survey: a plan (a floor-plan image, or a plain grid), and the places on it where you stood and
/// measured, each with the APs one scan heard there. Positions are in plan units: the image's pixels, or a
/// 1000 × 1000 grid. Saved as survey.json in its own folder, beside a copy of the plan image.
/// </summary>
public sealed class Survey
{
    public const double GridSize = 1000;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public DateTime Created { get; set; } = DateTime.UtcNow;

    /// <summary>The plan image's file name in the survey folder; null for the grid.</summary>
    public string? PlanImage { get; set; }
    public double PlanWidth { get; set; } = GridSize;
    public double PlanHeight { get; set; } = GridSize;

    public List<SurveyMark> Marks { get; set; } = new();

    [JsonIgnore] public string Folder => Path.Combine(SurveyStore.Root, Id);
    [JsonIgnore] public string? PlanImagePath => PlanImage is null ? null : Path.Combine(Folder, PlanImage);
}

/// <summary>A place on the plan where a scan was taken.</summary>
public sealed class SurveyMark
{
    public double X { get; set; }
    public double Y { get; set; }
    public DateTime Time { get; set; }
    public List<SurveyReading> Readings { get; set; } = new();
}

/// <summary>One AP as one scan heard it.</summary>
public sealed class SurveyReading
{
    public string Bssid { get; set; } = string.Empty;
    public string Ssid { get; set; } = string.Empty;
    public int Rssi { get; set; }
    public int Signal { get; set; }
    public int Channel { get; set; }
    public int FrequencyMhz { get; set; }
    public string Auth { get; set; } = string.Empty;
    public string Encryption { get; set; } = string.Empty;
}

/// <summary>Surveys on disk, each in its own folder under the app's data.</summary>
public static class SurveyStore
{
    private const string LastKey = "Survey_Last";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string Root => Path.Combine(FileSystem.AppDataDirectory, "surveys");

    /// <summary>The survey open last, to reopen with the page.</summary>
    public static string LastId
    {
        get => Preferences.Get(LastKey, string.Empty);
        set => Preferences.Set(LastKey, value);
    }

    /// <summary>Every survey, newest first.</summary>
    public static List<Survey> List()
    {
        var surveys = new List<Survey>();
        if (!Directory.Exists(Root)) return surveys;
        foreach (var dir in Directory.EnumerateDirectories(Root))
            if (Load(Path.GetFileName(dir)) is { } s) surveys.Add(s);
        return surveys.OrderByDescending(s => s.Created).ToList();
    }

    public static Survey? Load(string id)
    {
        try
        {
            var path = Path.Combine(Root, id, "survey.json");
            if (!File.Exists(path)) return null;
            var survey = JsonSerializer.Deserialize<Survey>(File.ReadAllText(path), Json);
            if (survey is not null) survey.Id = id;
            return survey;
        }
        catch (Exception ex)
        {
            DebugLog.Write($"[Survey] couldn't load {id}: {ex.Message}");
            return null;
        }
    }

    public static void Save(Survey survey)
    {
        Directory.CreateDirectory(survey.Folder);
        var path = Path.Combine(survey.Folder, "survey.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(survey, Json));
        File.Move(temp, path, overwrite: true);   // never leave a half-written survey
    }

    public static void Delete(Survey survey)
    {
        try { Directory.Delete(survey.Folder, recursive: true); } catch (IOException) { }
        if (LastId == survey.Id) LastId = string.Empty;
    }

    /// <summary>Copies a plan image into the survey, replacing any earlier one. The caller sets its size.</summary>
    public static async Task SetPlanImageAsync(Survey survey, Stream image, string fileName)
    {
        Directory.CreateDirectory(survey.Folder);
        if (survey.PlanImagePath is { } old) try { File.Delete(old); } catch (IOException) { }
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext)) ext = ".png";
        survey.PlanImage = "plan" + ext.ToLowerInvariant();
        await using var file = File.Create(survey.PlanImagePath!);
        await image.CopyToAsync(file);
    }
}
