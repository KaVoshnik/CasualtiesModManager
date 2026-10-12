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

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(mod.Version)) parts.Add("v" + mod.Version.TrimStart('v', 'V'));
        if (!string.IsNullOrWhiteSpace(mod.Author)) parts.Add(mod.Author);
        if (mod.Downloads > 0) parts.Add("скачиваний " + mod.Downloads.ToString("N0", CultureInfo.CurrentCulture));
        if (mod.Endorsements > 0) parts.Add("лайков " + mod.Endorsements.ToString("N0", CultureInfo.CurrentCulture));
        Meta = string.Join("  ·  ", parts);

        // в кратком описании Nexus бывают HTML-теги и сущности
        var text = Regex.Replace(mod.Summary ?? "", "<.*?>", " ");
        Summary = Regex.Replace(WebUtility.HtmlDecode(text), @"\s+", " ").Trim();
    }

    public NexusMod Mod { get; }
    public string Name => Mod.Name;
    public string Summary { get; }
    public string Meta { get; }
    public ImageSource? Picture { get; }

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

    public string ButtonText => Busy ? "Устанавливаю…" : Installed ? "Установлен" : "Установить";
    public bool CanInstall => !Busy && !Installed;

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

public partial class NexusWindow : Window
{
    private const int PageSize = 20;

    private readonly MainWindow _main;
    private readonly ModLibrary _lib;
    private readonly ObservableCollection<NexusCard> _cards = new();
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(450) };

    private NexusService.SortKind _sort = NexusService.SortKind.Popular;
    private int _generation;   // чтобы запоздавший ответ на старый запрос не затёр новый
    private int _offset;
    private int _total;

    public NexusWindow(MainWindow main, ModLibrary lib)
    {
        InitializeComponent();
        _main = main;
        _lib = lib;
        ModsList.ItemsSource = _cards;

        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await LoadAsync(reset: true);
        };

        RefreshAccount();
        HighlightSort();

        Loaded += async (_, _) =>
        {
            if (!NexusService.HasKey) await _main.EnsureNexusKeyAsync();
            RefreshAccount();
            await LoadAsync(reset: true);
        };
    }

    // ---------- Состояние ----------

    private Brush Res(string key) => (Brush)FindResource(key);

    private void SetStatus(string text, bool error = false)
    {
        StatusText.Text = text;
        StatusDot.Fill = Res(error ? "DangerBrush" : "MutedBrush");
    }

    /// <summary>Обновляет плашки аккаунта и nxm. Вызывается и из главного окна.</summary>
    public void RefreshAccount()
    {
        var s = _lib.Settings;
        if (NexusService.HasKey)
        {
            AccountDot.Fill = Res("OkBrush");
            var who = string.IsNullOrEmpty(s.NexusUser) ? "ключ задан" : s.NexusUser;
            AccountText.Text = $"Nexus: {who} · {(s.NexusPremium ? "Premium" : "бесплатный")}";
        }
        else
        {
            AccountDot.Fill = Res("DangerBrush");
            AccountText.Text = "Нужен API-ключ";
        }

        var nxm = false;
        try { nxm = NexusService.IsNxmHandlerOurs(); } catch { }
        NxmDot.Fill = Res(nxm ? "OkBrush" : "MutedBrush");
        NxmText.Text = "Приём nxm-ссылок: " + (nxm ? "вкл" : "выкл");

        FreeNote.Visibility = NexusService.HasKey && !s.NexusPremium ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Проставляет «Установлен» на карточках (после установки или удаления модов).</summary>
    public void RefreshInstalled()
    {
        foreach (var c in _cards) c.Installed = _lib.FindNexusMod(c.Mod.ModId) != null;
    }

    private void HighlightSort()
    {
        foreach (var b in new[] { SortPopular, SortNewest, SortUpdated, SortEndorsed })
        {
            var active = b.Tag as string == _sort.ToString();
            b.Foreground = Res(active ? "AccentBrush" : "TextBrush");
            b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    // ---------- Загрузка каталога ----------

    private async Task LoadAsync(bool reset)
    {
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
            EmptyText.Text = "Чтобы смотреть каталог, нужен личный API-ключ Nexus Mods. Нажми плашку «Нужен API-ключ».";
            EmptyText.Visibility = Visibility.Visible;
            SetStatus("API-ключ не задан");
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        SetStatus("Загружаю каталог…");
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
                EmptyText.Text = "Ничего не найдено";
                EmptyText.Visibility = Visibility.Visible;
            }

            SetStatus(page.Fallback
                ? "Поиск по всему каталогу сейчас недоступен — показаны подборки Nexus (по 10 модов)"
                : $"Показано {_cards.Count} из {_total}");
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
        if (await _main.PromptNexusKeyAsync())
        {
            RefreshAccount();
            await LoadAsync(reset: true);
        }
    }

    private void Nxm_Click(object sender, RoutedEventArgs e)
    {
        _main.ToggleNxmHandler();
        RefreshAccount();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
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
        catch (Exception ex) { SetStatus("Не удалось открыть браузер: " + ex.Message, error: true); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
