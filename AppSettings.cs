using System;
using System.IO;
using System.Text.Json;

namespace WarbandToBannerlordConverter;

public class AppSettings
{
    private const string AppFolderName = "WarbandToBannerlordConverter";
    private const string SettingsFileName = "settings.json";

    public string LastTerrainFolder { get; set; } = "";
    public string LastMissionJsonPath { get; set; } = "";
    public string LastSceneXscenePath { get; set; } = "";

    public static string SettingsPath
    {
        get
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, AppFolderName, SettingsFileName);
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();

            string json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        try
        {
            string directory = Path.GetDirectoryName(SettingsPath) ?? "";
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, options));
        }
        catch
        {
            // Settings should never block conversion work.
        }
    }
}
