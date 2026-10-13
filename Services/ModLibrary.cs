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
        NexusService.ApiKey = Settings.NexusApiKey;
    }

    /// <summary>Сохраняет API-ключ Nexus (пустая строка — забыть ключ).</summary>
    public void SetNexusKey(string key, string user = "", bool premium = false)
    {
        Settings.NexusApiKey = key.Trim();
        Settings.NexusUser = user;
        Settings.NexusPremium = premium;
        NexusService.ApiKey = Settings.NexusApiKey;
        Save();
    }

    /// <summary>Подставляет размер файла для отображения в карточке.</summary>
    public void RefreshInfo(ModEntry m)
    {
        try
        {
            var len = new FileInfo(SourcePath(m)).Length;
            m.SizeText = len switch
            {
                < 1024 => $"{len} {Loc.T("Size_B")}",
                < 1024 * 1024 => $"{len / 1024.0:0.#} {Loc.T("Size_KB")}",
                _ => $"{len / 1024.0 / 1024.0:0.#} {Loc.T("Size_MB")}",
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
    public ModEntry Add(string file, bool? enable = null, ModSource? source = null, string? name = null)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        if (ext != ".dll" && ext != ".zip")
            throw new InvalidOperationException(Loc.T("E_OnlyDllZip"));

        var mod = new ModEntry
        {
            Name = string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(file) : name.Trim(),
            SourceFile = Path.GetFileName(file),
            Source = source,
        };
        Directory.CreateDirectory(Path.Combine(ModsDir, mod.Id));
        File.Copy(file, SourcePath(mod), overwrite: true);
        RefreshInfo(mod);

        Settings.Mods.Add(mod);
        Save(); // сохраняем до установки: если установка упадёт, мод всё равно останется в списке
        if (enable ?? true) SetEnabled(mod, true);
        return mod;
    }

    // ---------- Обновления ----------

    /// <summary>Скачивает мод по ссылке (репозиторий GitHub или прямая ссылка), ставит и запоминает источник.</summary>
    public async Task<ModEntry> AddFromLinkAsync(string link)
    {
        var src = UpdateService.ParseLink(link);
        if (src.Type == "nexus")
            return (await InstallFromNexusAsync(src.NexusModId, null, null, null)).Mod;

        var info = src.Type == "github"
            ? await UpdateService.ResolveGitHubAsync(src, null, "")
            : new UpdateInfo { Url = src.Url, FileName = UpdateService.FileNameFromUrl(src.Url) };

        var tmpDir = Path.Combine(Path.GetTempPath(), "cmm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmpDir);
        try
        {
            var tmpFile = Path.Combine(tmpDir, info.FileName);
            await UpdateService.DownloadAsync(info.Url, tmpFile);

            src.Hash = UpdateService.HashFile(tmpFile);
            src.AssetName = src.Type == "github" ? info.FileName : "";
            src.Version = src.Type == "github" ? info.Version : DateTime.Now.ToString("yyyy-MM-dd");
            return Add(tmpFile, source: src);
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    public void SetSource(ModEntry mod, ModSource? source)
    {
        mod.Source = source;
        mod.Pending = null;
        mod.UpdateAvailable = false;
        mod.LatestVersion = "";
        mod.NotifySourceChanged();
        Save();
    }

    /// <summary>Проверяет моды с заданным источником. Возвращает (сколько обновлений найдено, сколько проверок не удалось).</summary>
    public async Task<(int Available, int Failed)> CheckUpdatesAsync(IEnumerable<ModEntry> mods)
    {
        int available = 0, failed = 0;
        foreach (var m in mods.Where(m => m.Source != null).ToList())
        {
            if (m.Status.StartsWith(Loc.T("S_ModCheckFailed", ""))) m.Status = "";
            try
            {
                var info = await UpdateService.CheckAsync(m);
                m.Pending = info;
                m.LatestVersion = info?.Version ?? "";
                m.UpdateAvailable = info != null;
                if (info != null) available++;
            }
            catch (Exception ex)
            {
                failed++;
                m.Status = Loc.T("S_ModCheckFailed", ex.Message);
            }
        }
        Save();
        return (available, failed);
    }

    /// <summary>Скачивает и ставит найденное обновление. Если что-то пошло не так — возвращает старую версию.</summary>
    public async Task UpdateAsync(ModEntry mod)
    {
        var info = mod.Pending ?? throw new InvalidOperationException(Loc.T("E_NoPendingUpdate"));
        var src = mod.Source ?? throw new InvalidOperationException(Loc.T("E_NoSource"));

        var tmpDir = NewTempDir();
        try
        {
            var url = info.Url;
            // Nexus: прямую ссылку без похода на сайт отдаёт только Premium
            if (src.Type == "nexus")
                url = await NexusService.GetDownloadUrlAsync(src.NexusModId, info.NexusFileId, null, null);

            var tmp = Path.Combine(tmpDir, SafeFileName(info.FileName));
            await UpdateService.DownloadAsync(url, tmp);

            var newHash = ReplaceFile(mod, tmp);

            src.Version = info.Version;
            src.AssetName = src.Type == "github" ? info.FileName : "";
            src.Hash = newHash;
            if (src.Type == "nexus") src.NexusFileId = info.NexusFileId;
            if (!string.IsNullOrEmpty(info.ETag)) src.ETag = info.ETag;

            mod.Pending = null;
            mod.UpdateAvailable = false;
            mod.LatestVersion = "";
            mod.NotifySourceChanged();
            Save();
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "cmm_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Имя файла из внешнего источника: только последний сегмент, без недопустимых символов.</summary>
    private static string SafeFileName(string name)
    {
        var n = Path.GetFileName(name.Replace('/', '\\'));
        foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        return string.IsNullOrWhiteSpace(n) ? "mod.zip" : n;
    }

    /// <summary>
    /// Подменяет файл мода новой версией (tmp). Если мод включён — переустанавливает в игру.
    /// При ошибке возвращает прежнюю версию. Возвращает SHA-256 нового файла.
    /// </summary>
    private string ReplaceFile(ModEntry mod, string tmp)
    {
        var ext = Path.GetExtension(tmp).ToLowerInvariant();
        if (ext != ".dll" && ext != ".zip")
            throw new InvalidOperationException(Loc.T("E_UnsupportedFile", Path.GetFileName(tmp)));
        var newHash = UpdateService.HashFile(tmp);

        var game = Settings.GameDir;
        var wasEnabled = mod.Enabled && GameLocator.IsGameDir(game);
        var oldName = mod.SourceFile;
        var oldPath = SourcePath(mod);
        var newPath = Path.Combine(ModsDir, mod.Id, Path.GetFileName(tmp));
        var backup = oldPath + ".bak";

        File.Copy(oldPath, backup, overwrite: true);
        try
        {
            if (wasEnabled) ModInstaller.Uninstall(game!, mod.InstalledFiles);

            File.Copy(tmp, newPath, overwrite: true);
            if (!newPath.Equals(oldPath, StringComparison.OrdinalIgnoreCase)) File.Delete(oldPath);
            mod.SourceFile = Path.GetFileName(tmp);

            if (wasEnabled) mod.InstalledFiles = ModInstaller.Install(game!, newPath);
        }
        catch
        {
            // откат на прежнюю версию
            try
            {
                var restored = Path.Combine(ModsDir, mod.Id, oldName);
                File.Copy(backup, restored, overwrite: true);
                if (!newPath.Equals(restored, StringComparison.OrdinalIgnoreCase) && File.Exists(newPath))
                    File.Delete(newPath);
                mod.SourceFile = oldName;
                if (wasEnabled) mod.InstalledFiles = ModInstaller.Install(game!, restored);
            }
            catch { /* откатить не вышло — оставляем как есть, ошибка ниже всё равно будет показана */ }
            throw;
        }
        finally
        {
            try { File.Delete(backup); } catch { }
        }

        RefreshInfo(mod);
        return newHash;
    }

    // ---------- Nexus Mods ----------

    /// <summary>Мод из библиотеки, который пришёл с Nexus (по id мода).</summary>
    public ModEntry? FindNexusMod(int modId) =>
        Settings.Mods.FirstOrDefault(m => m.Source is { Type: "nexus" } s && s.NexusModId == modId);

    /// <summary>
    /// Скачивает файл с Nexus и ставит мод. Если мод с Nexus уже есть в библиотеке — заменяет его файл.
    /// fileId == null → основной файл мода. key/expires — из nxm-ссылки (нужны бесплатным аккаунтам).
    /// </summary>
    public async Task<(ModEntry Mod, bool Updated)> InstallFromNexusAsync(
        int modId, long? fileId, string? nxmKey, long? expires, string? modName = null)
    {
        var list = await NexusService.GetFilesAsync(modId);
        var file = fileId != null
            ? list.Files.FirstOrDefault(f => f.FileId == fileId)
            : NexusService.PickMainFile(list);
        if (file == null)
            throw new InvalidOperationException(Loc.T(fileId != null ? "E_NexusFileGone" : "E_NexusNoFiles"));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".dll" && ext != ".zip")
            throw new InvalidOperationException(
                Loc.T("E_NexusBadType", file.FileName));

        var url = await NexusService.GetDownloadUrlAsync(modId, file.FileId, nxmKey, expires);

        var tmpDir = NewTempDir();
        try
        {
            var tmp = Path.Combine(tmpDir, SafeFileName(file.FileName));
            await UpdateService.DownloadAsync(url, tmp);

            var existing = FindNexusMod(modId);
            if (existing?.Source != null)
            {
                var hash = ReplaceFile(existing, tmp);
                existing.Source.Version = file.Version;
                existing.Source.NexusFileId = file.FileId;
                existing.Source.Hash = hash;
                existing.Pending = null;
                existing.UpdateAvailable = false;
                existing.LatestVersion = "";
                existing.NotifySourceChanged();
                Save();
                return (existing, true);
            }

            var name = modName;
            if (string.IsNullOrWhiteSpace(name))
            {
                try { name = (await NexusService.GetModAsync(modId)).Name; }
                catch { name = null; } // не критично: возьмём имя из файла
            }

            var src = new ModSource
            {
                Type = "nexus",
                Url = NexusService.ModPageUrl(modId),
                Version = file.Version,
                NexusModId = modId,
                NexusFileId = file.FileId,
                Hash = UpdateService.HashFile(tmp),
            };
            return (Add(tmp, source: src, name: name), false);
        }
        finally
        {
            try { Directory.Delete(tmpDir, true); } catch { }
        }
    }

    /// <summary>
    /// Находит моды, поставленные вручную в BepInEx/plugins, и берёт их под управление:
    /// копирует в библиотеку (оригиналы в игре не трогает) и помечает как включённые.
    /// Учитываются одиночные .dll в корне plugins и подпапки, где есть хотя бы один .dll.
    /// </summary>
    public List<ModEntry> SyncInstalled()
    {
        var added = new List<ModEntry>();
        var game = Settings.GameDir;
        if (!GameLocator.IsGameDir(game)) return added;

        var plugins = BepInExInstaller.PluginsDir(game!);
        if (!Directory.Exists(plugins)) return added;

        // Что уже известно менеджеру (пути относительно папки игры, в нижнем регистре, со слэшем \)
        static string Norm(string p) => p.Replace('/', '\\').ToLowerInvariant();
        var known = new HashSet<string>(
            Settings.Mods.SelectMany(m => m.InstalledFiles).Select(Norm));

        string Rel(string full) => Path.GetRelativePath(game!, full);

        // 1) одиночные .dll в корне plugins
        foreach (var dll in Directory.EnumerateFiles(plugins, "*.dll", SearchOption.TopDirectoryOnly))
        {
            var rel = Rel(dll);
            if (known.Contains(Norm(rel))) continue;

            var mod = new ModEntry
            {
                Name = Path.GetFileNameWithoutExtension(dll),
                SourceFile = Path.GetFileName(dll),
                Enabled = true,
                InstalledFiles = new List<string> { rel },
            };
            Directory.CreateDirectory(Path.Combine(ModsDir, mod.Id));
            File.Copy(dll, SourcePath(mod), overwrite: true);
            RefreshInfo(mod);
            Settings.Mods.Add(mod);
            added.Add(mod);
        }

        // 2) подпапки с модами
        foreach (var dir in Directory.EnumerateDirectories(plugins, "*", SearchOption.TopDirectoryOnly))
        {
            var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();
            if (files.Count == 0 || !files.Any(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                continue; // папка без .dll — это данные какого-то мода, сама по себе модом не считается

            var rels = files.Select(Rel).ToList();
            if (rels.All(r => known.Contains(Norm(r)))) continue;

            var folderName = Path.GetFileName(dir);
            var mod = new ModEntry
            {
                Name = folderName,
                SourceFile = folderName + ".zip",
                Enabled = true,
                InstalledFiles = rels,
            };
            Directory.CreateDirectory(Path.Combine(ModsDir, mod.Id));
            // в архив кладём папку целиком (Папка/файлы...) — при установке она снова ляжет в plugins/
            ZipFile.CreateFromDirectory(dir, SourcePath(mod),
                System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: true);
            RefreshInfo(mod);
            Settings.Mods.Add(mod);
            added.Add(mod);
        }

        if (added.Count > 0) Save();
        return added;
    }

    public void SetEnabled(ModEntry mod, bool enabled)
    {
        var game = Settings.GameDir;
        if (!GameLocator.IsGameDir(game))
        {
            mod.Enabled = false;
            throw new InvalidOperationException(Loc.T("E_NoGameFolder"));
        }

        if (enabled)
        {
            if (!BepInExInstaller.IsInstalled(game!))
                throw new InvalidOperationException(Loc.T("E_BepMissing"));
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
            manifest.Mods.Add(new PackMod { Name = m.Name, SourceFile = entryName, Enabled = m.Enabled, Source = m.Source });
        }
        var e = zip.CreateEntry("manifest.json");
        using var w = new StreamWriter(e.Open());
        w.Write(JsonSerializer.Serialize(manifest, Json));
    }

    public List<ModEntry> ImportPack(string packPath)
    {
        var added = new List<ModEntry>();
        using var zip = ZipFile.OpenRead(packPath);
        var mEntry = zip.GetEntry("manifest.json") ?? throw new InvalidOperationException(Loc.T("E_NotAPack"));
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
                Source = pm.Source,
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
