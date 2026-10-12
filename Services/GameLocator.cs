using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CasualtiesModManager.Services;

public static class GameLocator
{
    public const string ExeName = "CasualtiesUnknown.exe";
    public const int SteamAppId = 4576510;
    private static readonly string[] FolderNames = { "Casualties Unknown Demo", "Casualties Unknown" };

    public static bool IsGameDir(string? dir) =>
        !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, ExeName));

    /// <summary>Ищет игру во всех библиотеках Steam. Возвращает null, если не нашёл.</summary>
    public static string? Find()
    {
        foreach (var lib in SteamLibraries())
            foreach (var name in FolderNames)
            {
                var dir = Path.Combine(lib, "steamapps", "common", name);
                if (IsGameDir(dir)) return dir;
            }
        return null;
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var result = new List<string>();
        string? steam = null;
        try
        {
            steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        }
        catch { /* реестра нет — пропускаем */ }

        steam ??= @"C:\Program Files (x86)\Steam";
        steam = steam.Replace('/', '\\');
        result.Add(steam);

        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                result.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>BepInEx известен проблемой: не грузит моды, если в пути к игре есть не-ASCII символы.</summary>
    public static bool HasNonAsciiPath(string dir) => dir.Any(c => c > 127);
}
