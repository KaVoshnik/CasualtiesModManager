using System.IO.Compression;

namespace CasualtiesModManager.Services;

/// <summary>
/// Раскладывает файлы мода в папку игры так же, как это делали бы руками:
///  A) в архиве есть папка BepInEx/ → содержимое идёт от корня игры;
///  B) в архиве верхний уровень plugins/ или config/ → кладём внутрь BepInEx/;
///  C) иначе всё идёт прямо в BepInEx/plugins/ (одиночный .dll или zip «распакуй в plugins»).
/// </summary>
public static class ModInstaller
{
    private static readonly string[] BepInExSubdirs = { "plugins", "config", "patchers", "core" };

    public static List<string> Install(string gameDir, string sourcePath)
    {
        var installed = new List<string>();
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();

        if (ext == ".zip")
        {
            using var zip = ZipFile.OpenRead(sourcePath);
            var entries = zip.Entries
                .Where(e => !string.IsNullOrEmpty(e.Name))          // не папки
                .Where(e => !e.FullName.StartsWith("__MACOSX", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var mode = DetectMode(entries.Select(e => e.FullName));
            foreach (var e in entries)
            {
                var rel = MapPath(e.FullName, mode);
                if (rel == null) continue;
                var dest = SafeCombine(gameDir, rel);
                // Настройки пользователя не затираем: если конфиг уже есть, оставляем как есть
                if (IsConfig(rel) && File.Exists(dest))
                {
                    installed.Add(rel);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                e.ExtractToFile(dest, overwrite: true);
                installed.Add(rel);
            }
        }
        else
        {
            // одиночный файл (обычно .dll)
            var rel = Path.Combine("BepInEx", "plugins", Path.GetFileName(sourcePath));
            var dest = SafeCombine(gameDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(sourcePath, dest, overwrite: true);
            installed.Add(rel);
        }

        return installed;
    }

    public static void Uninstall(string gameDir, IEnumerable<string> installedFiles)
    {
        foreach (var rel in installedFiles)
        {
            if (IsConfig(rel)) continue; // конфиги — данные пользователя, при выключении и обновлении их не трогаем
            try
            {
                var path = SafeCombine(gameDir, rel);
                if (File.Exists(path)) File.Delete(path);
                // подчищаем пустые папки вверх до BepInEx/plugins (сам plugins не трогаем)
                var dir = Path.GetDirectoryName(path);
                var stop = Path.GetFullPath(BepInExInstaller.PluginsDir(gameDir));
                while (!string.IsNullOrEmpty(dir) &&
                       Path.GetFullPath(dir).StartsWith(stop + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                       Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                    dir = Path.GetDirectoryName(dir);
                }
            }
            catch { /* файл занят игрой и т.п. — пропускаем */ }
        }
    }

    private static bool IsConfig(string rel) =>
        rel.Replace('/', '\\').StartsWith(@"BepInEx\config\", StringComparison.OrdinalIgnoreCase);

    private enum Mode { FromGameRoot, IntoBepInEx, IntoPlugins }

    private static string[] Segments(string path) =>
        path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static Mode DetectMode(IEnumerable<string> names)
    {
        var list = names.Select(Segments).ToList();
        if (list.Any(s => s.Any(x => x.Equals("BepInEx", StringComparison.OrdinalIgnoreCase))))
            return Mode.FromGameRoot;
        if (list.Any(s => s.Length > 1 && BepInExSubdirs.Contains(s[0], StringComparer.OrdinalIgnoreCase)))
            return Mode.IntoBepInEx;
        return Mode.IntoPlugins;
    }

    private static string? MapPath(string entryName, Mode mode)
    {
        var segs = Segments(entryName);
        if (segs.Length == 0) return null;

        switch (mode)
        {
            case Mode.FromGameRoot:
                var i = Array.FindIndex(segs, x => x.Equals("BepInEx", StringComparison.OrdinalIgnoreCase));
                if (i < 0) return null; // файл вне BepInEx/ в таком архиве (readme и т.п.) пропускаем
                return Path.Combine(segs[i..]);
            case Mode.IntoBepInEx:
                if (!BepInExSubdirs.Contains(segs[0], StringComparer.OrdinalIgnoreCase)) return null;
                return Path.Combine(new[] { "BepInEx" }.Concat(segs).ToArray());
            default:
                return Path.Combine(new[] { "BepInEx", "plugins" }.Concat(segs).ToArray());
        }
    }

    /// <summary>Защита от zip-slip: итоговый путь обязан лежать внутри папки игры.</summary>
    private static string SafeCombine(string root, string rel)
    {
        var full = Path.GetFullPath(Path.Combine(root, rel));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Loc.T("E_BadArchivePath", rel));
        return full;
    }
}
