using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CasualtiesModManager.Services;

/// <summary>Мод в каталоге Nexus (то, что показываем в списке).</summary>
public class NexusMod
{
    public int ModId { get; set; }
    public string Name { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Version { get; set; } = "";
    public string Author { get; set; } = "";
    public string PictureUrl { get; set; } = "";
    public long Downloads { get; set; }
    public long Endorsements { get; set; }
    public string PageUrl => NexusService.ModPageUrl(ModId);
}

/// <summary>Файл мода на Nexus (из files.json).</summary>
public class NexusFile
{
    public long FileId { get; set; }
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Version { get; set; } = "";
    public string Category { get; set; } = "";
    public bool IsPrimary { get; set; }
    public long Uploaded { get; set; }
    public long SizeKb { get; set; }
}

public record NexusUser(string Name, bool IsPremium);

/// <summary>Разобранная ссылка nxm://scavprototype/mods/123/files/456?key=…&amp;expires=…</summary>
public record NxmLink(string Game, int ModId, long FileId, string? Key, long? Expires);

/// <summary>Ошибка Nexus с понятным пользователю текстом.</summary>
public class NexusException : Exception
{
    public NexusException(string message) : base(message) { }
}

/// <summary>Работа с Nexus Mods: публичный API v1 (+ GraphQL v2 для поиска) и разбор nxm-ссылок.</summary>
public static class NexusService
{
    public const string GameDomain = "scavprototype";
    private const string AppName = "CasualtiesModManager";
    private const string AppVersion = "1.1.0";
    private const string Api = "https://api.nexusmods.com/v1/";
    private const string GraphQl = "https://api.nexusmods.com/v2/graphql";

    private static readonly HttpClient Http = CreateClient();

    /// <summary>Личный API-ключ пользователя. Задаётся менеджером из настроек.</summary>
    public static string ApiKey { get; set; } = "";

    public static bool HasKey => !string.IsNullOrWhiteSpace(ApiKey);

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppName}/{AppVersion}");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Application-Name", AppName);
        c.DefaultRequestHeaders.TryAddWithoutValidation("Application-Version", AppVersion);
        return c;
    }

    public static string ModPageUrl(int modId) => $"https://www.nexusmods.com/{GameDomain}/mods/{modId}";

    /// <summary>Страница файлов мода: кнопка «Slow download» / «Mod manager download» отправит nxm-ссылку менеджеру.</summary>
    public static string FileDownloadPageUrl(int modId, long fileId) =>
        $"{ModPageUrl(modId)}?tab=files&file_id={fileId}&nmm=1";

    // ---------- Запросы ----------

    private static async Task<JsonDocument> GetJsonAsync(string url)
    {
        if (!HasKey) throw new NexusException(Loc.T("E_NexusNeedKey"));

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("apikey", ApiKey.Trim());
        using var resp = await Http.SendAsync(req);
        var body = await resp.Content.ReadAsStringAsync();

        if (resp.IsSuccessStatusCode) return JsonDocument.Parse(body);

        throw resp.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new NexusException(Loc.T("E_NexusKeyRejected")),
            HttpStatusCode.Forbidden => new NexusException(
                Loc.T("E_NexusFreeNoDirect")),
            HttpStatusCode.NotFound => new NexusException(Loc.T("E_NexusNotFound")),
            (HttpStatusCode)429 => new NexusException(Loc.T("E_NexusRateLimit")),
            _ => new NexusException(Loc.T("E_NexusHttp", (int)resp.StatusCode)),
        };
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static long Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>Проверяет ключ и возвращает имя пользователя и признак Premium.</summary>
    public static async Task<NexusUser> ValidateAsync()
    {
        using var doc = await GetJsonAsync(Api + "users/validate.json");
        var r = doc.RootElement;
        return new NexusUser(Str(r, "name"), Bool(r, "is_premium"));
    }

    // ---------- Каталог ----------

    public enum SortKind { Popular, Newest, Updated, Endorsed }

    public record CatalogPage(List<NexusMod> Mods, int Total, bool Fallback);

    /// <summary>GraphQL v2 сломался (изменилась схема и т.п.) — дальше работаем только через v1.</summary>
    private static bool _graphQlUnavailable;

    /// <summary>
    /// Страница каталога. Основной путь — GraphQL v2 (есть поиск и постраничная загрузка).
    /// Если он не отвечает, падаем на v1: там только три подборки по 10 модов и без поиска.
    /// </summary>
    public static async Task<CatalogPage> BrowseAsync(string query, SortKind sort, int offset, int count)
    {
        if (!_graphQlUnavailable)
        {
            try { return await BrowseGraphQlAsync(query, sort, offset, count); }
            catch (NexusException) { throw; }            // ключ, лимиты — это не вина схемы
            catch (Exception) { _graphQlUnavailable = true; }
        }
        return await BrowseV1Async(query, sort, offset);
    }

    private static async Task<CatalogPage> BrowseGraphQlAsync(string query, SortKind sort, int offset, int count)
    {
        var filter = new Dictionary<string, object>
        {
            ["gameDomainName"] = new[] { new { value = GameDomain } },
        };
        if (!string.IsNullOrWhiteSpace(query))
            filter["name"] = new[] { new { value = query.Trim(), op = "WILDCARD" } };

        var sortField = sort switch
        {
            SortKind.Newest => "createdAt",
            SortKind.Updated => "updatedAt",
            SortKind.Endorsed => "endorsements",
            _ => "downloads",
        };

        var payload = new
        {
            query = "query($filter: ModsFilter, $offset: Int, $count: Int, $sort: [ModsSort!]) {" +
                    " mods(filter: $filter, offset: $offset, count: $count, sort: $sort) {" +
                    " totalCount nodes { modId name summary version author downloads endorsements pictureUrl } } }",
            variables = new
            {
                filter,
                offset,
                count,
                sort = new[] { new Dictionary<string, object> { [sortField] = new { direction = "DESC" } } },
            },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, GraphQl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("apikey", ApiKey.Trim());
        using var resp = await Http.SendAsync(req);

        if (resp.StatusCode == HttpStatusCode.Unauthorized) throw new NexusException(Loc.T("E_NexusKeyRejected"));
        if ((int)resp.StatusCode == 429) throw new NexusException(Loc.T("E_NexusRateLimit"));
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("mods", out var mods) || mods.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(Loc.T("E_GraphQlBad"));

        var list = new List<NexusMod>();
        foreach (var n in mods.GetProperty("nodes").EnumerateArray())
            list.Add(new NexusMod
            {
                ModId = (int)Num(n, "modId"),
                Name = Str(n, "name"),
                Summary = Str(n, "summary"),
                Version = Str(n, "version"),
                Author = Str(n, "author"),
                PictureUrl = Str(n, "pictureUrl"),
                Downloads = Num(n, "downloads"),
                Endorsements = Num(n, "endorsements"),
            });
        return new CatalogPage(list, (int)Num(mods, "totalCount"), false);
    }

    /// <summary>Запасной вариант: подборки v1 (по 10 модов). Поиск — по загруженному.</summary>
    private static async Task<CatalogPage> BrowseV1Async(string query, SortKind sort, int offset)
    {
        if (offset > 0) return new CatalogPage(new(), 0, true); // у v1 нет страниц

        var endpoint = sort switch
        {
            SortKind.Newest => "latest_added",
            SortKind.Updated => "latest_updated",
            _ => "trending",
        };
        using var doc = await GetJsonAsync($"{Api}games/{GameDomain}/mods/{endpoint}.json");
        var list = doc.RootElement.EnumerateArray().Select(ParseV1Mod).Where(m => m.ModId > 0).ToList();
        if (!string.IsNullOrWhiteSpace(query))
            list = list.Where(m => m.Name.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase) ||
                                   m.Summary.Contains(query.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return new CatalogPage(list, list.Count, true);
    }

    private static NexusMod ParseV1Mod(JsonElement e) => new()
    {
        ModId = (int)Num(e, "mod_id"),
        Name = Str(e, "name"),
        Summary = Str(e, "summary"),
        Version = Str(e, "version"),
        Author = Str(e, "author"),
        PictureUrl = Str(e, "picture_url"),
        Downloads = Num(e, "mod_downloads"),
        Endorsements = Num(e, "endorsement_count"),
    };

    public static async Task<NexusMod> GetModAsync(int modId)
    {
        using var doc = await GetJsonAsync($"{Api}games/{GameDomain}/mods/{modId}.json");
        return ParseV1Mod(doc.RootElement);
    }

    // ---------- Файлы ----------

    public record FileList(List<NexusFile> Files, Dictionary<long, long> Replaced);

    public static async Task<FileList> GetFilesAsync(int modId)
    {
        using var doc = await GetJsonAsync($"{Api}games/{GameDomain}/mods/{modId}/files.json");
        var files = new List<NexusFile>();
        if (doc.RootElement.TryGetProperty("files", out var arr))
            foreach (var f in arr.EnumerateArray())
            {
                var version = Str(f, "mod_version");
                if (version == "") version = Str(f, "version");
                files.Add(new NexusFile
                {
                    FileId = Num(f, "file_id"),
                    Name = Str(f, "name"),
                    FileName = Str(f, "file_name"),
                    Version = version,
                    Category = Str(f, "category_name").ToUpperInvariant(),
                    IsPrimary = Bool(f, "is_primary"),
                    Uploaded = Num(f, "uploaded_timestamp"),
                    SizeKb = Num(f, "size_kb"),
                });
            }

        // file_updates: старый файл → новый (так Nexus помнит, что файл заменили новой версией)
        var replaced = new Dictionary<long, long>();
        if (doc.RootElement.TryGetProperty("file_updates", out var upd))
            foreach (var u in upd.EnumerateArray())
            {
                var o = Num(u, "old_file_id");
                var n = Num(u, "new_file_id");
                if (o != 0 && n != 0) replaced[o] = n;
            }
        return new FileList(files, replaced);
    }

    private static bool IsLive(NexusFile f) => f.Category is not ("OLD_VERSION" or "ARCHIVED" or "DELETED");

    /// <summary>Какой файл ставить по умолчанию: основной, иначе самый свежий из живых.</summary>
    public static NexusFile? PickMainFile(FileList list)
    {
        var live = list.Files.Where(IsLive).ToList();
        return live.Where(f => f.Category == "MAIN").OrderByDescending(f => f.IsPrimary).ThenByDescending(f => f.Uploaded).FirstOrDefault()
               ?? live.OrderByDescending(f => f.Uploaded).FirstOrDefault();
    }

    /// <summary>Самая свежая версия уже установленного файла (идём по цепочке замен).</summary>
    public static NexusFile? PickLatestOf(FileList list, long currentFileId)
    {
        var id = currentFileId;
        for (var i = 0; i < 20 && list.Replaced.TryGetValue(id, out var next); i++) id = next;
        var found = list.Files.FirstOrDefault(f => f.FileId == id);
        return found != null && IsLive(found) ? found : PickMainFile(list);
    }

    // ---------- Скачивание ----------

    /// <summary>
    /// Прямая ссылка на файл. Premium получает её сам; бесплатному аккаунту нужны key и expires
    /// из nxm-ссылки (они появляются, когда человек нажимает кнопку скачивания на сайте).
    /// </summary>
    public static async Task<string> GetDownloadUrlAsync(int modId, long fileId, string? key, long? expires)
    {
        var url = $"{Api}games/{GameDomain}/mods/{modId}/files/{fileId}/download_link.json";
        if (!string.IsNullOrEmpty(key) && expires != null)
            url += $"?key={Uri.EscapeDataString(key)}&expires={expires}";

        using var doc = await GetJsonAsync(url);
        foreach (var l in doc.RootElement.EnumerateArray())
        {
            var uri = Str(l, "URI");
            if (uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return uri;
        }
        throw new NexusException(Loc.T("E_NexusNoLink"));
    }

    // ---------- Ссылки ----------

    private static readonly Regex NxmRx = new(
        @"^nxm://(?<game>[^/]+)/mods/(?<mod>\d+)/files/(?<file>\d+)$", RegexOptions.IgnoreCase);

    private static readonly Regex PageRx = new(
        @"^/(?<game>[^/]+)/mods/(?<mod>\d+)", RegexOptions.IgnoreCase);

    public static bool IsNxm(string? s) => s != null && s.StartsWith("nxm://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Разбирает nxm-ссылку на файл. Ссылки на коллекции и чужие игры отклоняются с понятной ошибкой.</summary>
    public static NxmLink ParseNxm(string link)
    {
        link = link.Trim().Trim('"');
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || !uri.Scheme.Equals("nxm", StringComparison.OrdinalIgnoreCase))
            throw new NexusException(Loc.T("E_NotNxm"));

        var m = NxmRx.Match(uri.GetLeftPart(UriPartial.Path));
        if (!m.Success) throw new NexusException(Loc.T("E_NxmNotMod"));
        if (!m.Groups["game"].Value.Equals(GameDomain, StringComparison.OrdinalIgnoreCase))
            throw new NexusException(Loc.T("E_NxmWrongGame", m.Groups["game"].Value));

        string? key = null;
        long? expires = null;
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            var v = Uri.UnescapeDataString(kv[1]);
            if (kv[0].Equals("key", StringComparison.OrdinalIgnoreCase)) key = v;
            else if (kv[0].Equals("expires", StringComparison.OrdinalIgnoreCase) && long.TryParse(v, out var ex)) expires = ex;
        }
        return new NxmLink(GameDomain, int.Parse(m.Groups["mod"].Value), long.Parse(m.Groups["file"].Value), key, expires);
    }

    /// <summary>Ссылка на страницу мода на nexusmods.com → id мода (или null, если это не наша игра/страница).</summary>
    public static int? TryParseModPage(Uri uri)
    {
        if (!uri.Host.EndsWith("nexusmods.com", StringComparison.OrdinalIgnoreCase)) return null;
        var m = PageRx.Match(uri.AbsolutePath);
        if (!m.Success || !m.Groups["game"].Value.Equals(GameDomain, StringComparison.OrdinalIgnoreCase)) return null;
        return int.Parse(m.Groups["mod"].Value);
    }

    // ---------- Регистрация обработчика nxm:// (только для текущего пользователя) ----------

    private const string NxmKey = @"Software\Classes\nxm";

    public static string? GetNxmHandlerCommand()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(NxmKey + @"\shell\open\command");
        return k?.GetValue(null) as string;
    }

    public static bool IsNxmHandlerOurs()
    {
        var cmd = GetNxmHandlerCommand();
        var exe = Environment.ProcessPath;
        return cmd != null && exe != null && cmd.Contains(exe, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Делает менеджер обработчиком nxm://. Возвращает прежнюю команду (чтобы вернуть её при отключении).</summary>
    public static string? RegisterNxmHandler()
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException(Loc.T("E_NoExePath"));
        if (Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Loc.T("E_DotnetRun"));
        var previous = IsNxmHandlerOurs() ? null : GetNxmHandlerCommand();

        using var root = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(NxmKey);
        root.SetValue(null, "URL:NXM Protocol");
        root.SetValue("URL Protocol", "");
        using var cmd = root.CreateSubKey(@"shell\open\command");
        cmd.SetValue(null, $"\"{exe}\" \"%1\"");
        return previous;
    }

    /// <summary>Убирает наш обработчик; если раньше был другой (Vortex, MO2) — возвращает его.</summary>
    public static void UnregisterNxmHandler(string? previousCommand)
    {
        if (!IsNxmHandlerOurs()) return;
        if (!string.IsNullOrEmpty(previousCommand))
        {
            using var cmd = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(NxmKey + @"\shell\open\command");
            cmd.SetValue(null, previousCommand);
        }
        else
        {
            Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(NxmKey, throwOnMissingSubKey: false);
        }
    }
}
