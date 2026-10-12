using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace CasualtiesModManager.Services;

/// <summary>Поиск и скачивание обновлений: релизы GitHub и прямые ссылки на файл.</summary>
public static class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("CasualtiesModManager/1.0");
        return c;
    }

    private record Asset(string Name, string Url);
    private record Release(string Tag, List<Asset> Assets);

    // ---------- Разбор ссылки ----------

    public static ModSource ParseLink(string link)
    {
        link = link.Trim();
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("Это не похоже на ссылку");

        // Страница мода на Nexus Mods (только наша игра)
        if (NexusService.TryParseModPage(uri) is int nexusId)
            return new ModSource { Type = "nexus", Url = NexusService.ModPageUrl(nexusId), NexusModId = nexusId };

        var path = uri.AbsolutePath;
        var isGitHub = uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                       uri.Host.Equals("www.github.com", StringComparison.OrdinalIgnoreCase);
        var isFile = path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                     path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        // Ссылка на файл релиза или на страницу репозитория → следим за последним релизом репозитория
        if (isGitHub && (!isFile || path.Contains("/releases/", StringComparison.OrdinalIgnoreCase)))
        {
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var repo = parts[1];
                if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) repo = repo[..^4];
                return new ModSource { Type = "github", Url = $"https://github.com/{parts[0]}/{repo}" };
            }
        }

        if (isFile)
        {
            var url = uri.AbsoluteUri;
            // страница файла на GitHub → ссылка на сам файл
            if (isGitHub && path.Contains("/blob/", StringComparison.Ordinal))
                url = url.Replace("/blob/", "/raw/");
            return new ModSource { Type = "url", Url = url };
        }

        throw new InvalidOperationException("Нужна ссылка на репозиторий GitHub, страницу мода на Nexus Mods или прямая ссылка на .dll / .zip");
    }

    public static string FileNameFromUrl(string url) =>
        Uri.UnescapeDataString(Path.GetFileName(new Uri(url).AbsolutePath));

    // ---------- GitHub ----------

    private static async Task<Release> GetLatestReleaseAsync(string repoUrl)
    {
        var parts = new Uri(repoUrl).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var api = $"https://api.github.com/repos/{parts[0]}/{parts[1]}/releases/latest";

        using var req = new HttpRequestMessage(HttpMethod.Get, api);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var resp = await Http.SendAsync(req);

        if (resp.StatusCode == HttpStatusCode.NotFound)
            throw new InvalidOperationException("У репозитория нет релизов — вставь прямую ссылку на .dll / .zip");
        if (resp.StatusCode == HttpStatusCode.Forbidden || (int)resp.StatusCode == 429)
            throw new InvalidOperationException("Лимит запросов GitHub, попробуй через час");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        var assets = new List<Asset>();
        if (root.TryGetProperty("assets", out var arr))
            foreach (var a in arr.EnumerateArray())
                assets.Add(new Asset(
                    a.GetProperty("name").GetString() ?? "",
                    a.GetProperty("browser_download_url").GetString() ?? ""));
        return new Release(tag, assets);
    }

    private static Asset PickAsset(Release r, string? preferred, string modName)
    {
        var cands = r.Assets
            .Where(a => a.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                        a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (cands.Count == 0)
            throw new InvalidOperationException("В релизе нет файлов .dll или .zip");

        if (!string.IsNullOrEmpty(preferred))
        {
            var p = cands.FirstOrDefault(a => a.Name.Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (p != null) return p;
        }
        if (cands.Count == 1) return cands[0];

        // Несколько файлов: берём тот, что больше похож на имя мода
        static string Key(string s) =>
            new string(Path.GetFileNameWithoutExtension(s).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var k = Key(modName);
        return cands.FirstOrDefault(a => k.Length > 0 && (Key(a.Name).Contains(k) || k.Contains(Key(a.Name))))
               ?? cands[0];
    }

    /// <summary>Последний релиз репозитория вместе с файлом, который надо скачать.</summary>
    public static async Task<UpdateInfo> ResolveGitHubAsync(ModSource src, string? preferredAsset, string modName)
    {
        var rel = await GetLatestReleaseAsync(src.Url);
        var asset = PickAsset(rel, preferredAsset, modName);
        return new UpdateInfo { Version = rel.Tag, Url = asset.Url, FileName = asset.Name };
    }

    // ---------- Проверка ----------

    /// <summary>Возвращает описание обновления или null, если мод актуален.</summary>
    public static async Task<UpdateInfo?> CheckAsync(ModEntry mod)
    {
        var src = mod.Source ?? throw new InvalidOperationException("У мода нет источника");

        if (src.Type == "nexus")
        {
            var files = await NexusService.GetFilesAsync(src.NexusModId);
            var latest = src.NexusFileId != 0
                ? NexusService.PickLatestOf(files, src.NexusFileId)
                : NexusService.PickMainFile(files);
            if (latest == null || latest.FileId == src.NexusFileId) return null;
            // файл не отслеживался (источник задан вручную), но версия та же — обновлять нечего
            if (src.NexusFileId == 0 && src.Version != "" && latest.Version == src.Version) return null;
            return new UpdateInfo { Version = latest.Version, FileName = latest.FileName, NexusFileId = latest.FileId };
        }

        if (src.Type == "github")
        {
            var rel = await GetLatestReleaseAsync(src.Url);
            if (rel.Tag == src.Version) return null;
            var asset = PickAsset(rel, src.AssetName, mod.Name);
            return new UpdateInfo { Version = rel.Tag, Url = asset.Url, FileName = asset.Name };
        }

        // Прямая ссылка: условный запрос по ETag, а если сервер его не отдаёт — сравниваем хэш файла
        using var req = new HttpRequestMessage(HttpMethod.Get, src.Url);
        if (!string.IsNullOrEmpty(src.ETag)) req.Headers.TryAddWithoutValidation("If-None-Match", src.ETag);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        if (resp.StatusCode == HttpStatusCode.NotModified) return null;
        resp.EnsureSuccessStatusCode();

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var etag = resp.Headers.ETag?.Tag ?? "";
        if (hash == src.Hash)
        {
            src.ETag = etag;
            return null;
        }

        var date = resp.Content.Headers.LastModified?.LocalDateTime ?? DateTime.Now;
        return new UpdateInfo
        {
            Version = "от " + date.ToString("dd.MM.yyyy"),
            Url = src.Url,
            FileName = FileNameFromUrl(src.Url),
            ETag = etag,
            Hash = hash,
        };
    }

    // ---------- Скачивание ----------

    public static async Task DownloadAsync(string url, string destPath)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        await using var fs = File.Create(destPath);
        await resp.Content.CopyToAsync(fs);
    }

    public static string HashFile(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
