using System.Reflection;

namespace CasualtiesModManager.Services;

/// <summary>Версия и адрес репозитория самого менеджера.</summary>
public static class AppInfo
{
    /// <summary>GitHub-репозиторий, в релизах которого лежат новые версии менеджера.</summary>
    public const string Repo = "KaVoshnik/CasualtiesModManager";

    /// <summary>Текущая версия (из csproj), приведённая к виду 1.2.0.0 для честного сравнения.</summary>
    public static Version Current { get; } = Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0, 0));

    /// <summary>«1.2.0» — для заголовков запросов и интерфейса.</summary>
    public static string VersionText { get; } = $"{Current.Major}.{Current.Minor}.{Current.Build}";

    public static Version Normalize(Version v) =>
        new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
}
