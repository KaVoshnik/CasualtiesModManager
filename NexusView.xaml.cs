using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CasualtiesModManager.Services;

namespace CasualtiesModManager;

/// <summary>Карточка мода в каталоге.</summary>
public class NexusCard : INotifyPropertyChanged
{
    private bool _installed;
    private bool _busy;

    public NexusCard(NexusMod mod, bool installed)
    {
        Mod = mod;
        _installed = installed;
        Picture = LoadPicture(mod.PictureUrl);

        // в кратком описании Nexus бывают HTML-теги и сущности
        var text = Regex.Replace(mod.Summary ?? "", "<.*?>", " ");
        Summary = Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
    }

    public NexusMod Mod { get; }
    public string Name => Mod.Name;
    public string Summary { get; }
    public ImageSource? Picture { get; }

    /// <summary>«v1.2 · автор · 1 234 downloads · 56 likes» — собирается заново при смене языка.</summary>
    public string Meta
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(Mod.Version)) parts.Add("v" + Mod.Version.TrimStart('v', 'V'));
            if (!string.IsNullOrWhiteSpace(Mod.Author)) parts.Add(Mod.Author);
            if (Mod.Downloads > 0) parts.Add(Loc.T("Nx_Downloads", Mod.Downloads.ToString("N0", CultureInfo.CurrentCulture)));
            if (Mod.Endorsements > 0) parts.Add(Loc.T("Nx_Likes", Mod.Endorsements.ToString("N0", CultureInfo.CurrentCulture)));
            return string.Join("  ·  ", parts);
        }
    }

    public bool Installed
    {
        get => _installed;
        set { _installed = value; Changed(); Changed(nameof(ButtonText)); Changed(nameof(CanInstall)); }
    }

    public bool Busy
    {
        get => _busy;
        set { _busy = value; Changed(); Changed(nameof(ButtonText)); Changed(nameof(CanInstall)); }
    }

    public string ButtonText => Loc.T(Busy ? "Nx_Installing" : Installed ? "Nx_Installed" : "Nx_Install");
    public bool CanInstall => !Busy && !Installed;

    public void RefreshLocalized()
    {
        Changed(nameof(Meta));
        Changed(nameof(ButtonText));
    }

    private static ImageSource? LoadPicture(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = uri;
            bi.DecodePixelWidth = 128;   // значок 64 px, полноразмерную картинку держать в памяти незачем
            bi.EndInit();
            return bi;
        }
        catch { return null; }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? n = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Вкладка «Nexus» в главном окне: каталог модов с поиском и установкой.</summary>
public partial class NexusView : UserControl
{
    private const int PageSize = 20;

    private MainWindow? _main;
    private ModLibrary? _lib;
    private readonly ObservableCollection<NexusCard> _cards = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(450) };

    private NexusService.SortKind _sort = NexusService.SortKind.Popular;
    private int _generation;   // чтобы запоздавший ответ на старый запрос не затёр новый
    private int _offset;
    private int _total;
    private bool _started;

    public NexusView()
    {
        InitializeComponent();
        ModsList.ItemsSource = _cards;

        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await LoadAsync(reset: true);
        };
    }

    /// <summary>Подключает вкладку к главному окну (вызывается один раз при создании окна).</summary>
    public void Init(MainWindow main, ModLibrary lib)
    {
        _main = main;
        _lib = lib;
        RefreshAccount();
        HighlightSort();
    }

    /// <summary>Вызывается при первом показе вкладки: спрашивает ключ, если его нет, и грузит каталог.</summary>
    public async Task EnsureLoadedAsync()
    {
        if (_started || _main == null) return;
        _started = true;

        if (!NexusService.HasKey) await _main.EnsureNexusKeyAsync();
        RefreshAccount();
        await LoadAsync(reset: true);
    }

    // ---------- Состояние ----------

    private Brush Res(string key) => (Brush)FindResource(key);

    private void SetStatus(string text, bool error = false) => _main?.ReportStatus(text, error);

    /// <summary>Обновляет плашки аккаунта и nxm, подписи карточек. Вызывается и из главного окна.</summary>
    public void RefreshAccount()
    {
        if (_lib == null) return;
        var s = _lib.Settings;
        if (NexusService.HasKey)
        {
            AccountDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "OkBrush");
            var who = string.IsNullOrEmpty(s.NexusUser) ? Loc.T("Nx_KeySet") : s.NexusUser;
            AccountText.Text = Loc.T("Nx_Account", who, Loc.T(s.NexusPremium ? "Nx_Premium" : "Nx_AccountFree"));
        }
        else
        {
            AccountDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "DangerBrush");
            AccountText.Text = Loc.T("Nx_NeedKey");
        }

        var nxm = false;
        try { nxm = NexusService.IsNxmHandlerOurs(); } catch { }
        NxmDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, nxm ? "OkBrush" : "MutedBrush");
        NxmText.Text = Loc.T(nxm ? "Nx_NxmOn" : "Nx_NxmOff");

        FreeNote.Visibility = NexusService.HasKey && !s.NexusPremium ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Язык сменился: обновляем всё, что собрано в коде.</summary>
    public void OnLanguageChanged()
    {
        RefreshAccount();
        foreach (var c in _cards) c.RefreshLocalized();
        if (EmptyText.Visibility == Visibility.Visible && _cards.Count == 0)
            EmptyText.Text = Loc.T(NexusService.HasKey ? "Nx_NothingFound" : "Nx_NoKeyHint");
    }

    /// <summary>Проставляет «Installed» на карточках (после установки или удаления модов).</summary>
    public void RefreshInstalled()
    {
        if (_lib == null) return;
        foreach (var c in _cards) c.Installed = _lib.FindNexusMod(c.Mod.ModId) != null;
    }

    private void HighlightSort()
    {
        foreach (var b in new[] { SortPopular, SortNewest, SortUpdated, SortEndorsed })
        {
            var active = b.Tag as string == _sort.ToString();
            b.SetResourceReference(Control.ForegroundProperty, active ? "AccentBrush" : "TextBrush");
            b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    // ---------- Загрузка каталога ----------

    private async Task LoadAsync(bool reset)
    {
        if (_lib == null) return;

        var gen = ++_generation;
        if (reset)
        {
            _cards.Clear();
            _offset = 0;
            _total = 0;
        }
        MoreButton.Visibility = Visibility.Collapsed;

        if (!NexusService.HasKey)
        {
            EmptyText.Text = Loc.T("Nx_NoKeyHint");
            EmptyText.Visibility = Visibility.Visible;
            SetStatus(Loc.T("Nx_NoKeyStatus"));
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        SetStatus(Loc.T("Nx_Loading"));
        try
        {
            var page = await NexusService.BrowseAsync(SearchBox.Text, _sort, _offset, PageSize);
            if (gen != _generation) return;

            foreach (var m in page.Mods.Where(m => m.ModId > 0))
                _cards.Add(new NexusCard(m, _lib.FindNexusMod(m.ModId) != null));
            _offset += page.Mods.Count;
            _total = page.Total;

            MoreButton.Visibility = !page.Fallback && page.Mods.Count > 0 && _offset < _total
                ? Visibility.Visible : Visibility.Collapsed;

            if (_cards.Count == 0)
            {
                EmptyText.Text = Loc.T("Nx_NothingFound");
                EmptyText.Visibility = Visibility.Visible;
            }

            SetStatus(page.Fallback ? Loc.T("Nx_Fallback") : Loc.T("Nx_Shown", _cards.Count, _total));
        }
        catch (Exception ex)
        {
            if (gen != _generation) return;
            SetStatus(ex.Message, error: true);
            if (_cards.Count == 0)
            {
                EmptyText.Text = ex.Message;
                EmptyText.Visibility = Visibility.Visible;
            }
        }
    }

    private async void More_Click(object sender, RoutedEventArgs e) => await LoadAsync(reset: false);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_started) return; // пока вкладку не открывали, ничего не грузим
        _debounce.Stop();
        _debounce.Start();
    }

    private async void Sort_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !Enum.TryParse<NexusService.SortKind>(tag, out var kind)) return;
        _sort = kind;
        HighlightSort();
        await LoadAsync(reset: true);
    }

    // ---------- Действия ----------

    private async void Account_Click(object sender, RoutedEventArgs e)
    {
        if (_main != null && await _main.PromptNexusKeyAsync())
        {
            RefreshAccount();
            await LoadAsync(reset: true);
        }
    }

    private void Nxm_Click(object sender, RoutedEventArgs e)
    {
        _main?.ToggleNxmHandler();
        RefreshAccount();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_main == null || _lib == null) return;
        if (sender is not Button { Tag: NexusCard card } || !card.CanInstall) return;

        card.Busy = true;
        try { await _main.InstallNexusModAsync(card.Mod); }
        finally
        {
            card.Busy = false;
            card.Installed = _lib.FindNexusMod(card.Mod.ModId) != null;
            RefreshAccount(); // мог измениться статус Premium
        }
    }

    private void OpenPage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NexusCard card }) return;
        try { Process.Start(new ProcessStartInfo(card.Mod.PageUrl) { UseShellExecute = true }); }
        catch (Exception ex) { SetStatus(Loc.T("Nx_BrowserFailed", ex.Message), error: true); }
    }
}
