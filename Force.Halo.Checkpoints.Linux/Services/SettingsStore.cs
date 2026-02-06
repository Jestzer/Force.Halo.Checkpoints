using System;
using System.IO;
using System.Text.Json;

namespace Force.Halo.Checkpoints.Linux.Services;

internal static class SettingsStore
{
    private const string FileName = "settings.json";
    private const string AppFolderName = "force-halo-checkpoints";

    public static HotkeySettings Load()
    {
        try
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return HotkeySettings.Empty;
            }

            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<HotkeySettings>(json) ?? HotkeySettings.Empty;
        }
        catch
        {
            return HotkeySettings.Empty;
        }
    }

    public static void Save(HotkeySettings settings)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(settings.Note))
            {
                settings = settings with
                {
                    Note = "To re-enable X11 warnings, set the SuppressX11* flags to false."
                };
            }

            string path = GetSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch
        {
            // If we can't save settings, keep the app functional.
        }
    }

    private static string GetSettingsPath()
    {
        string configRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(configRoot, AppFolderName, FileName);
    }
}
