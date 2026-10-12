using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace CasualtiesModManager;

/// <summary>Откуда мод берёт обновления.</summary>
public class ModSource
{
    /// <summary>"github" — последний релиз репозитория; "url" — прямая ссылка на файл.</summary>
    public string Type { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>github: тег релиза; url: дата загрузки. Пусто = версия установленного мода неизвестна.</summary>
    public string Version { get; set; } = "";

    /// <summary>github: имя файла из релиза, который мы берём.</summary>
    public string AssetName { get; set; } = "";

    /// <summary>url: SHA-256 установленного файла и ETag сервера — для поиска изменений.</summary>
    public string Hash { get; set; } = "";
    public string ETag { get; set; } = "";

    /// <summary>nexus: id мода и id установленного файла на Nexus Mods.</summary>
    public int NexusModId { get; set; }
    public long NexusFileId { get; set; }
}

/// <summary>Найденное обновление (живёт только в памяти).</summary>
public class UpdateInfo
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ETag { get; set; } = "";
    public string Hash { get; set; } = "";
    public long NexusFileId { get; set; }
}

public class ModEntry : INotifyPropertyChanged
{
    private bool _enabled;
    private string _status = "";
    private string _sourceFile = "";
    private string _sizeText = "";
    private bool _updateAvailable;
    private string _latestVersion = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Имя исходного файла (.dll или .zip), лежит в библиотеке: mods/&lt;Id&gt;/&lt;SourceFile&gt;</summary>
    public string SourceFile
    {
        get => _sourceFile;
        set { _sourceFile = value; OnChanged(); OnChanged(nameof(Kind)); }
    }

    /// <summary>Файлы, реально скопированные в игру (пути относительно папки игры). Нужны для чистого удаления.</summary>
    public List<string> InstalledFiles { get; set; } = new();

    /// <summary>Источник обновлений (если задан).</summary>
    public ModSource? Source { get; set; }

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnChanged(); }
    }

    [JsonIgnore]
    public string Status
    {
        get => _status;
        set { _status = value; OnChanged(); }
    }

    /// <summary>Размер файла мода для отображения (заполняется библиотекой).</summary>
    [JsonIgnore]
    public string SizeText
    {
        get => _sizeText;
        set { _sizeText = value; OnChanged(); }
    }

    /// <summary>DLL / ZIP — подпись на значке карточки.</summary>
    [JsonIgnore]
    public string Kind => Path.GetExtension(SourceFile).TrimStart('.').ToUpperInvariant();

    [JsonIgnore]
    public bool UpdateAvailable
    {
        get => _updateAvailable;
        set { _updateAvailable = value; OnChanged(); }
    }

    [JsonIgnore]
    public string LatestVersion
    {
        get => _latestVersion;
        set { _latestVersion = value; OnChanged(); }
    }

    /// <summary>Найденное, но ещё не установленное обновление.</summary>
    [JsonIgnore]
    public UpdateInfo? Pending { get; set; }

    /// <summary>Подпись источника на карточке: «GitHub · v5.5».</summary>
    [JsonIgnore]
    public string SourceText
    {
        get
        {
            if (Source == null) return "";
            var kind = Source.Type switch { "github" => "GitHub", "nexus" => "Nexus", _ => "Ссылка" };
            return string.IsNullOrEmpty(Source.Version) ? $"{kind} · версия неизвестна" : $"{kind} · {Source.Version}";
        }
    }

    public void NotifySourceChanged() => OnChanged(nameof(SourceText));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class AppSettings
{
    public string? GameDir { get; set; }

    /// <summary>Личный API-ключ Nexus Mods. Лежит только здесь и в сборки (.cumods) не попадает.</summary>
    public string NexusApiKey { get; set; } = "";
    public string NexusUser { get; set; } = "";
    public bool NexusPremium { get; set; }

    /// <summary>Какой обработчик nxm:// был до нас — вернём его, если пользователь отключит приём ссылок.</summary>
    public string? NxmPreviousHandler { get; set; }

    public List<ModEntry> Mods { get; set; } = new();
}

/// <summary>Манифест внутри файла сборки (.cumods).</summary>
public class PackManifest
{
    public int Version { get; set; } = 1;
    public List<PackMod> Mods { get; set; } = new();
}

public class PackMod
{
    public string Name { get; set; } = "";
    public string SourceFile { get; set; } = "";
    public bool Enabled { get; set; } = true;

    /// <summary>Источник обновлений передаётся вместе со сборкой — у друга моды обновляются так же.</summary>
    public ModSource? Source { get; set; }
}
