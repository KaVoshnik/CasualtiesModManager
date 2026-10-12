using System.IO.Compression;
using System.Net.Http;

namespace CasualtiesModManager.Services;

public static class BepInExInstaller
{
    // Версия, которую используют моды Casualties: Unknown (BepInEx 5, win_x64).
    private const string Url =
        "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.4/BepInEx_win_x64_5.4.23.4.zip";

    public static string PluginsDir(string gameDir) => Path.Combine(gameDir, "BepInEx", "plugins");

    public static bool IsInstalled(string gameDir) =>
        Directory.Exists(Path.Combine(gameDir, "BepInEx", "core")) &&
        File.Exists(Path.Combine(gameDir, "winhttp.dll"));

    public static async Task InstallAsync(string gameDir)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var tmp = Path.Combine(Path.GetTempPath(), "bepinex_" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            var bytes = await http.GetByteArrayAsync(Url);
            await File.WriteAllBytesAsync(tmp, bytes);
            ZipFile.ExtractToDirectory(tmp, gameDir, overwriteFiles: true);
            Directory.CreateDirectory(PluginsDir(gameDir));
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
