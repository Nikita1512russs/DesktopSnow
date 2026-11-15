using System.Text.Json;

namespace DesktopSnow;

internal sealed class Settings
{
    public bool Enabled { get; set; } = true;
    public int Intensity { get; set; } = 55;
    public int Opacity { get; set; } = 92;
    public bool Windows { get; set; } = true;
    public bool Ground { get; set; } = true;
    public bool Melt { get; set; } = true;
    public bool Cursor { get; set; } = true;
    public bool Snowmen { get; set; } = true;
    public bool HideFromCapture { get; set; }
    public bool Plow { get; set; } = true;
    public int PlowMinutes { get; set; } = 3;
    public bool ReduceLoad { get; set; } = true;
    public bool ShowOnTaskbar { get; set; } = true;

    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopSnow");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public Settings Copy() => (Settings)MemberwiseClone();

    public static Settings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Settings();
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
            settings.Intensity = Math.Clamp(settings.Intensity, 0, 100);
            settings.Opacity = Math.Clamp(settings.Opacity, 0, 100);
            settings.PlowMinutes = Math.Clamp(settings.PlowMinutes, 1, 60);
            return settings;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            Log(error.Message);
            return new Settings();
        }
    }

    public bool Save()
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string temporary = FilePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporary, FilePath, overwrite: true);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Log(error.Message);
            return false;
        }
    }

    public static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            string path = Path.Combine(DirectoryPath, "errors.log");
            if (File.Exists(path) && new FileInfo(path).Length > 64 * 1024)
                File.Move(path, path + ".old", overwrite: true);
            File.AppendAllText(path, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }
}
