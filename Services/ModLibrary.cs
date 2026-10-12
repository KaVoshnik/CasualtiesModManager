using System.IO.Compression;
using System.Text.Json;

namespace CasualtiesModManager.Services;

/// <summary>Хранит моды в %AppData%\CasualtiesModManager и ставит/снимает их в игре.</summary>
public class ModLibrary
{
    public const string PackExtension = ".cumods";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CasualtiesModManager");
    private string SettingsPath => Path.Combine(_root, "settings.json");
    private string ModsDir => Path.Combine(_root, "mods");

    public AppSettings Settings { get; private set; } = new();

    public void Load()
    {
        Directory.CreateDirectory(ModsDir);
        if (File.Exists(SettingsPath))
        {
            try { Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new(); }
            catch { Settings = new(); }
        }
        foreach (var m in Settings.Mods) RefreshInfo(m);
    }

    /// <summary>Подставляет размер файла для отображения в карточке.</summary>
    public void RefreshInfo(ModEntry m)
    {
        try
        {
            var len = new FileInfo(SourcePath(m)).Length;
            m.SizeText = len switch
            {
                < 1024 => $"{len} Б",
                < 1024 * 1024 => $"{len / 1024.0:0.#} КБ",
                _ => $"{len / 1024.0 / 1024.0:0.#} МБ",
            };
        }
        catch { m.SizeText = ""; }
    }

    public void Save()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, Json));
    }

    private string SourcePath(ModEntry m) => Path.Combine(ModsDir, m.Id, m.SourceFile);

    /// <summary>Добавляет .dll или .zip в библиотеку (копирует). Если игра задана — сразу устанавливает.</summary>
    public ModEntry Add(string file, bool? enable = null)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (ext != ".dll" && ext != ".zip")
            throw new InvalidOperationException("Поддерживаются только .dll и .zip");

        var mod = new ModEntry
        {
            Name = Path.GetFileNameWithoutExtension(file),
            SourceFile = Path.GetFileName(file),
        };
        Directory.CreateDirectory(Path.Combine(ModsDir, mod.Id));
        File.Copy(file, SourcePath(mod), overwrite: true);
        RefreshInfo(mod);

        Settings.Mods.Add(mod);
        Save(); // сохраняем до установки: если установка упадёт, мод всё равно останется в списке
        if (enable ?? true) SetEnabled(mod, true);
        return mod;
    }

    public void SetEnabled(ModEntry mod, bool enabled)
    {
        var game = Settings.GameDir;
        if (!GameLocator.IsGameDir(game))
        {
            mod.Enabled = false;
            throw new InvalidOperationException("Папка игры не выбрана");
        }

        if (enabled)
        {
            if (!BepInExInstaller.IsInstalled(game!))
                throw new InvalidOperationException("BepInEx не установлен — нажми «Установить BepInEx»");
            mod.InstalledFiles = ModInstaller.Install(game!, SourcePath(mod));
        }
        else
        {
            ModInstaller.Uninstall(game!, mod.InstalledFiles);
            mod.InstalledFiles = new();
        }
        mod.Enabled = enabled;
        Save();
    }

    public void Remove(ModEntry mod)
    {
        if (mod.Enabled && GameLocator.IsGameDir(Settings.GameDir))
            ModInstaller.Uninstall(Settings.GameDir!, mod.InstalledFiles);
        try { Directory.Delete(Path.Combine(ModsDir, mod.Id), true); } catch { }
        Settings.Mods.Remove(mod);
        Save();
    }

    /// <summary>Применяет состояние всех модов заново (после смены папки игры, обновления игры и т.п.).</summary>
    public void Reapply()
    {
        foreach (var m in Settings.Mods.Where(m => m.Enabled).ToList())
            SetEnabled(m, true);
    }

    // ---------- Сборки (modpack) для друзей ----------

    public void ExportPack(string packPath, IEnumerable<ModEntry> mods)
    {
        if (File.Exists(packPath)) File.Delete(packPath);
        var manifest = new PackManifest();
        using var zip = ZipFile.Open(packPath, ZipArchiveMode.Create);
        var i = 0;
        foreach (var m in mods)
        {
            var entryName = $"mods/{i++}_{m.SourceFile}";
            zip.CreateEntryFromFile(SourcePath(m), entryName);
            manifest.Mods.Add(new PackMod { Name = m.Name, SourceFile = entryName, Enabled = m.Enabled });
        }
        var e = zip.CreateEntry("manifest.json");
        using var w = new StreamWriter(e.Open());
        w.Write(JsonSerializer.Serialize(manifest, Json));
    }

    public List<ModEntry> ImportPack(string packPath)
    {
        var added = new List<ModEntry>();
        using var zip = ZipFile.OpenRead(packPath);
        var mEntry = zip.GetEntry("manifest.json") ?? throw new InvalidOperationException("Это не файл сборки (нет manifest.json)");
        PackManifest manifest;
        using (var r = new StreamReader(mEntry.Open()))
            manifest = JsonSerializer.Deserialize<PackManifest>(r.ReadToEnd()) ?? new();

        foreach (var pm in manifest.Mods)
        {
            var e = zip.GetEntry(pm.SourceFile);
            if (e == null) continue;

            var mod = new ModEntry
            {
                Name = pm.Name,
                // имя без префикса «N_»
                SourceFile = Path.GetFileName(pm.SourceFile) is var f && f.IndexOf('_') > 0 ? f[(f.IndexOf('_') + 1)..] : Path.GetFileName(pm.SourceFile),
            };
            Directory.CreateDirectory(Path.Combine(ModsDir, mod.Id));
            e.ExtractToFile(SourcePath(mod), overwrite: true);
            RefreshInfo(mod);
            Settings.Mods.Add(mod);
            added.Add(mod);

            if (pm.Enabled)
            {
                try { SetEnabled(mod, true); } catch { /* игра/BepInEx не готовы — мод останется выключенным */ }
            }
        }
        Save();
        return added;
    }
}
