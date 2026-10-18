using System.Text.Json;

namespace AirGlass.Core;

public sealed class AppSettings
{
    public string DeviceName { get; set; } = "AirGlass";
    public string Resolution { get; set; } = "1920x1080";
    public bool LowLatency { get; set; }
    public bool UsePin { get; set; }
    public string Pin { get; set; } = "";
    public int BasePort { get; set; } = 7000;
    public bool StartWithWindows { get; set; }
    public bool FirewallTipShown { get; set; }

    public static string DefaultDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AirGlass");

    public static AppSettings Load() => Load(Path.Combine(DefaultDirectory, "settings.json"));

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch
        {
            // A corrupt settings file should never block startup.
        }
        return new AppSettings();
    }

    public void Save() => Save(Path.Combine(DefaultDirectory, "settings.json"));

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
