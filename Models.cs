using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace CasualtiesModManager;

public class ModEntry : INotifyPropertyChanged
{
    private bool _enabled;
    private string _status = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";

    /// <summary>Имя исходного файла (.dll или .zip), лежит в библиотеке: mods/&lt;Id&gt;/&lt;SourceFile&gt;</summary>
    public string SourceFile { get; set; } = "";

    /// <summary>Файлы, реально скопированные в игру (пути относительно папки игры). Нужны для чистого удаления.</summary>
    public List<string> InstalledFiles { get; set; } = new();

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

    private string _sizeText = "";

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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class AppSettings
{
    public string? GameDir { get; set; }
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
}
