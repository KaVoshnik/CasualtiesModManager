using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CasualtiesModManager.Services;

/// <summary>Найденная новая версия самого менеджера.</summary>
public record SelfUpdate(Version Version, string Tag, string AssetName, string AssetUrl, string? Sha256, string Notes);

/// <summary>
/// Автообновление менеджера по релизам GitHub.
/// Работающий exe Windows переименовать можно, а перезаписать нельзя — поэтому старый файл уходит в «.old»,
/// на его место кладётся новый, и менеджер перезапускается. «.old» удаляется при следующем запуске.
/// </summary>
public static class SelfUpdater
{
    public const string WaitPidArg = "--wait-pid";
    private const string OldSuffix = ".old";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("CasualtiesModManager/" + AppInfo.VersionText);
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    private static string? ExePath => Environment.ProcessPath;

    /// <summary>Обновляться можно только когда запущен сам CasualtiesModManager.exe (не «dotnet run»).</summary>
    public static bool CanSelfUpdate =>
        ExePath is { } p && !Path.GetFileName(p).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase);

    private static string AppDir => Path.GetDirectoryName(ExePath!)!;

    // ---------- Проверка ----------

    /// <summary>Возвращает новую версию или null, если установлена последняя.</summary>
    public static async Task<SelfUpdate?> CheckAsync(CancellationToken ct = default)
    {
        using var resp = await Http.GetAsync($"https://api.github.com/repos/{AppInfo.Repo}/releases/latest", ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // релизов ещё нет
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(Loc.T("E_GitHubStatus", (int)resp.StatusCode));

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var ver = ParseVersion(tag);
        if (ver == null || ver <= AppInfo.Current) return null;

        var assets = new List<(string Name, string Url, string? Sha)>();
        if (root.TryGetProperty("assets", out var arr))
            foreach (var a in arr.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var url = a.GetProperty("browser_download_url").GetString() ?? "";
                string? sha = null;
                if (a.TryGetProperty("digest", out var d) && d.GetString() is { } ds &&
                    ds.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    sha = ds[7..];
                assets.Add((name, url, sha));
            }

        var pick = PickAsset(assets);
        if (pick == null) throw new InvalidOperationException(Loc.T("E_NoAppAsset", tag));

        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        return new SelfUpdate(ver, tag, pick.Value.Name, pick.Value.Url, pick.Value.Sha, notes);
    }

    /// <summary>«v1.3.0» → 1.3.0.0; мусор → null.</summary>
    public static Version? ParseVersion(string tag)
    {
        var m = Regex.Match(tag, @"\d+(\.\d+){1,3}");
        return m.Success && Version.TryParse(m.Value, out var v) ? AppInfo.Normalize(v) : null;
    }

    private static (string Name, string Url, string? Sha)? PickAsset(List<(string Name, string Url, string? Sha)> assets)
    {
        var exe = Path.GetFileName(ExePath ?? "CasualtiesModManager.exe");
        bool Is(string n, string ext) => n.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
        bool Ours(string n) => n.Contains("CasualtiesModManager", StringComparison.OrdinalIgnoreCase);

        // 1) exe с точно таким же именем; 2) любой наш exe; 3) наш zip; 4) единственный exe/zip
        foreach (var a in assets) if (a.Name.Equals(exe, StringComparison.OrdinalIgnoreCase)) return a;
        foreach (var a in assets) if (Is(a.Name, ".exe") && Ours(a.Name)) return a;
        foreach (var a in assets) if (Is(a.Name, ".zip") && Ours(a.Name)) return a;
        var rest = assets.Where(a => Is(a.Name, ".exe") || Is(a.Name, ".zip")).ToList();
        return rest.Count == 1 ? rest[0] : null;
    }

    // ---------- Установка ----------

    /// <summary>Скачивает релиз и подменяет файлы. После успеха нужно вызвать <see cref="Restart"/>.</summary>
    public static async Task DownloadAndApplyAsync(SelfUpdate u, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!CanSelfUpdate) throw new InvalidOperationException(Loc.T("E_SelfUpdateUnsupported"));

        var tmp = Path.Combine(Path.GetTempPath(), "cmm_update_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            var file = Path.Combine(tmp, SafeName(u.AssetName));
            await DownloadAsync(u.AssetUrl, file, progress, ct);

            if (u.Sha256 != null)
            {
                await using var fs = File.OpenRead(file);
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
                if (!hash.Equals(u.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(Loc.T("E_HashMismatch"));
            }

            // что именно ставим: (откуда, куда относительно папки программы)
            var files = new List<(string Src, string Rel)>();
            if (file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var dir = Path.Combine(tmp, "x");
                ExtractSafe(file, dir);
                var root = dir;
                // если всё лежит в одной папке верхнего уровня — заходим в неё
                var subs = Directory.GetDirectories(dir);
                if (Directory.GetFiles(dir).Length == 0 && subs.Length == 1) root = subs[0];
                foreach (var f in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                    files.Add((f, Path.GetRelativePath(root, f)));

                var exeName = Path.GetFileName(ExePath!);
                if (!files.Any(f => f.Rel.Equals(exeName, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException(Loc.T("E_NoExeInZip", exeName));
            }
            else
            {
                files.Add((file, Path.GetFileName(ExePath!)));
            }

            if (new FileInfo(files.First(f => f.Rel.Equals(Path.GetFileName(ExePath!), StringComparison.OrdinalIgnoreCase)).Src).Length < 100_000)
                throw new InvalidOperationException(Loc.T("E_HashMismatch")); // подозрительно маленький «exe»

            Apply(files);
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    private static void Apply(List<(string Src, string Rel)> files)
    {
        var done = new List<(string Target, string? Backup)>();
        try
        {
            foreach (var (src, rel) in files)
            {
                var target = Path.GetFullPath(Path.Combine(AppDir, rel));
                if (!target.StartsWith(AppDir, StringComparison.OrdinalIgnoreCase)) continue; // защита от «../»
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                string? backup = null;
                if (File.Exists(target))
                {
                    backup = target + OldSuffix;
                    try { if (File.Exists(backup)) File.Delete(backup); } catch { }
                    File.Move(target, backup, overwrite: true);   // для работающего exe это разрешено
                }
                done.Add((target, backup));
                File.Copy(src, target, overwrite: true);
            }
        }
        catch
        {
            // откат: возвращаем всё как было
            foreach (var (target, backup) in Enumerable.Reverse(done))
            {
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    if (backup != null && File.Exists(backup)) File.Move(backup, target);
                }
                catch { }
            }
            throw;
        }
    }

    private static void ExtractSafe(string zip, string dest)
    {
        Directory.CreateDirectory(dest);
        var root = Path.GetFullPath(dest) + Path.DirectorySeparatorChar;
        using var z = ZipFile.OpenRead(zip);
        foreach (var e in z.Entries)
        {
            if (string.IsNullOrEmpty(e.Name)) continue; // папка
            var path = Path.GetFullPath(Path.Combine(dest, e.FullName));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            e.ExtractToFile(path, true);
        }
    }

    private static async Task DownloadAsync(string url, string file, IProgress<double>? progress, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(Loc.T("E_NotALink"));

        using var resp = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? 0;
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(file);

        var buf = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buf, ct)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n), ct);
            read += n;
            if (total > 0) progress?.Report((double)read / total);
        }
    }

    private static string SafeName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    // ---------- Перезапуск и уборка ----------

    /// <summary>Запускает новую версию; она подождёт, пока завершится этот процесс. Потом вызывающий закрывает программу.</summary>
    public static void Restart()
    {
        Process.Start(new ProcessStartInfo(ExePath!)
        {
            UseShellExecute = false,
            WorkingDirectory = AppDir,
            ArgumentList = { WaitPidArg, Environment.ProcessId.ToString() },
        });
    }

    /// <summary>Если запущены с «--wait-pid N» — ждём, пока старая копия закроется (чтобы не упереться в single-instance).</summary>
    public static void WaitForPreviousProcess(string[] args)
    {
        var i = Array.IndexOf(args, WaitPidArg);
        if (i < 0 || i + 1 >= args.Length || !int.TryParse(args[i + 1], out var pid)) return;
        try
        {
            using var p = Process.GetProcessById(pid);
            p.WaitForExit(15000);
        }
        catch { /* процесса уже нет */ }
    }

    /// <summary>Удаляет «.old» файлы, оставшиеся после обновления.</summary>
    public static void CleanupOld()
    {
        if (!CanSelfUpdate) return;
        try
        {
            foreach (var f in Directory.GetFiles(AppDir, "*" + OldSuffix, SearchOption.AllDirectories))
                try { File.Delete(f); } catch { }
        }
        catch { }
    }
}
