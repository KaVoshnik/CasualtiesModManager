using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace CasualtiesModManager.Services;

/// <summary>Текст для багрепорта: версии, состояние игры и BepInEx, список модов и хвост лога. Ключи и имя пользователя не попадают.</summary>
public static class DiagnosticsReport
{
    public static string Build(ModLibrary lib)
    {
        var s = lib.Settings;
        var sb = new StringBuilder();
        void Line(string t = "") => sb.AppendLine(t);

        Line($"Casualties Mod Manager {AppInfo.VersionText}");
        Line($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        Line($"OS: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
        Line($".NET: {RuntimeInformation.FrameworkDescription}");
        Line($"Language: {s.Language} · Theme: {s.Theme}");
        Line();

        Line("== Game ==");
        var dir = s.GameDir;
        if (!GameLocator.IsGameDir(dir))
        {
            Line("Game folder: NOT FOUND");
        }
        else
        {
            Line($"Game folder: {Redact(dir!)}");
            if (GameLocator.HasNonAsciiPath(dir!))
                Line("WARNING: path contains non-ASCII characters (BepInEx may fail to load mods)");
            Line($"Game exe: {Version(Path.Combine(dir!, GameLocator.ExeName))}");
            var bep = BepInExInstaller.IsInstalled(dir!);
            Line($"BepInEx: {(bep ? "installed" : "NOT INSTALLED")}");
            if (bep)
            {
                Line($"  core: {Version(Path.Combine(dir!, "BepInEx", "core", "BepInEx.dll"))}");
                Line($"  winhttp.dll: {(File.Exists(Path.Combine(dir!, "winhttp.dll")) ? "present" : "missing")}");
            }
        }
        Line();

        Line($"== Mods ({s.Mods.Count}) ==");
        foreach (var m in s.Mods)
        {
            var src = m.Source == null ? "no source" : $"{m.Source.Type} {m.Source.Version}".Trim();
            var missing = dir != null && m.Enabled
                ? m.InstalledFiles.Count(f => !File.Exists(Path.Combine(dir, f)))
                : 0;
            Line($"[{(m.Enabled ? "on " : "off")}] {m.Name} — {m.SourceFile}, {m.InstalledFiles.Count} file(s), {src}" +
                 (missing > 0 ? $"  !! {missing} installed file(s) missing" : ""));
        }
        Line();

        Line("== Nexus ==");
        Line($"API key: {(NexusService.HasKey ? "set" : "not set")}" +
             (NexusService.HasKey ? $" · {(s.NexusPremium ? "Premium" : "free account")}" : ""));
        string nxm;
        try { nxm = NexusService.IsNxmHandlerOurs() ? "this manager" : "other / not set"; }
        catch { nxm = "unknown"; }
        Line($"nxm:// handler: {nxm}");
        Line();

        if (GameLocator.IsGameDir(dir))
        {
            Line("== BepInEx log (last warnings/errors) ==");
            Line(LogTail(Path.Combine(dir!, "BepInEx", "LogOutput.log")));
        }
        return sb.ToString().TrimEnd();
    }

    private static string Version(string file)
    {
        try { return File.Exists(file) ? (FileVersionInfo.GetVersionInfo(file).FileVersion ?? "?") : "missing"; }
        catch { return "?"; }
    }

    /// <summary>Заменяет имя пользователя Windows в путях на %USERNAME%.</summary>
    private static string Redact(string text)
    {
        var user = Environment.UserName;
        return string.IsNullOrEmpty(user) ? text : text.Replace(user, "%USERNAME%", StringComparison.OrdinalIgnoreCase);
    }

    private static string LogTail(string path)
    {
        try
        {
            if (!File.Exists(path)) return "(no LogOutput.log — the game hasn't been launched with BepInEx yet)";

            string[] lines;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var r = new StreamReader(fs))
                lines = r.ReadToEnd().Split('\n');

            var important = lines
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Contains("[Error", StringComparison.Ordinal) || l.Contains("[Warning", StringComparison.Ordinal) ||
                            l.Contains("Exception", StringComparison.Ordinal) || l.StartsWith("   at ", StringComparison.Ordinal))
                .TakeLast(40)
                .Select(Redact);
            var text = string.Join("\n", important);
            return text.Length == 0 ? "(no warnings or errors)" : text;
        }
        catch (Exception ex)
        {
            return "(couldn't read the log: " + ex.Message + ")";
        }
    }
}
