using System.Text.Json;

namespace SimpleBassShakerRouter;

sealed class UserSettings
{
    public string? SourceId { get; set; }
    public string? ShakerId { get; set; }
    public int CutoffHz { get; set; } = 80;
    public int LevelPercent { get; set; } = 100;

    public void Clamp()
    {
        if (CutoffHz < 30 || CutoffHz > 200)
            CutoffHz = 80;
        if (LevelPercent < 0 || LevelPercent > 200)
            LevelPercent = 100;
    }
}

static class SettingsStore
{
    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SimpleBassShakerRouter",
        "settings.json");

    public static UserSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new UserSettings();

            UserSettings? settings = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath));
            settings ??= new UserSettings();
            settings.Clamp();
            return settings;
        }
        catch
        {
            return new UserSettings();
        }
    }

    public static void Save(UserSettings settings)
    {
        settings.Clamp();
        string? directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
