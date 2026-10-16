using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using CasualtiesModManager.Services;
using Microsoft.Win32;

namespace CasualtiesModManager;

public partial class MainWindow : Window
{
    private enum StatusKind { Info, Ok, Error }

    private readonly ModLibrary _lib = new();
    private readonly ObservableCollection<ModEntry> _mods = new();
    private readonly ICollectionView _view;

    public MainWindow()
    {
        _lib.Load();
        Loc.Init(_lib.Settings.Language);   // язык и тема — до InitializeComponent, чтобы окно сразу построилось правильно
        Theme.Apply(_lib.Settings.Theme);
        InitializeComponent();
        NexusPane.Init(this, _lib);
        Loc.LanguageChanged += OnLanguageChanged;

        if (!GameLocator.IsGameDir(_lib.Settings.GameDir))
        {
            _lib.Settings.GameDir = GameLocator.Find();
            _lib.Save();
        }

        foreach (var m in _lib.Settings.Mods) Attach(m);
        ModsList.ItemsSource = _mods;
        _mods.CollectionChanged += (_, _) => { UpdateEmpty(); UpdateCounts(); };

        _view = CollectionViewSource.GetDefaultView(_mods);
        _view.Filter = o =>
        {
            var q = SearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(q)) return true;
            var m = (ModEntry)o;
            return m.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || m.SourceFile.Contains(q, StringComparison.OrdinalIgnoreCase);
        };

        RefreshHeader();
        ScanInstalled(announce: false);
        UpdateEmpty();
        UpdateCounts();

        var dir = _lib.Settings.GameDir;
        if (!GameLocator.IsGameDir(dir))
            SetStatus(Loc.T("S_GameNotFoundHint"), StatusKind.Error);
        else if (!BepInExInstaller.IsInstalled(dir!))
            SetStatus(Loc.T("S_InstallBepHint"), StatusKind.Info);

        TabMods.IsChecked = true;

        // тихая проверка обновлений при запуске
        Loaded += async (_, _) =>
        {
            // программу могли запустить кликом по nxm-ссылке на сайте Nexus
            if (App.StartupNxm is { } nxm) await HandleNxmAsync(nxm);
            if (_lib.Settings.AutoCheckSelfUpdate) _ = CheckSelfUpdateAsync(announce: false);
            await CheckAllAsync(announce: false);
        };
    }

    // ---------- Служебное ----------

    private void Attach(ModEntry mod)
    {
        mod.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ModEntry.Enabled)) UpdateCounts(); };
        _mods.Add(mod);
    }

    private Brush Res(string key) => (Brush)FindResource(key);

    /// <summary>Подхватывает моды, уже лежащие в BepInEx/plugins (поставленные руками).</summary>
    private void ScanInstalled(bool announce)
    {
        try
        {
            var found = _lib.SyncInstalled();
            foreach (var m in found) Attach(m);

            if (found.Count > 0)
                SetStatus(Loc.T("S_FoundInGame", found.Count, Loc.Plural(found.Count, "mod")), StatusKind.Ok);
            else if (announce)
                SetStatus(Loc.T("S_NoNewMods"));
        }
        catch (Exception ex)
        {
            SetStatus(Loc.T("S_ScanFailed", ex.Message), StatusKind.Error);
        }
    }

    private void Rescan_Click(object sender, RoutedEventArgs e)
    {
        if (!GameLocator.IsGameDir(_lib.Settings.GameDir))
        {
            SetStatus(Loc.T("S_PickGameFirst"), StatusKind.Error);
            return;
        }
        ScanInstalled(announce: true);
    }

    private void SetStatus(string text, StatusKind kind = StatusKind.Info)
    {
        StatusText.Text = text;
        StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, kind switch
        {
            StatusKind.Ok => "OkBrush",
            StatusKind.Error => "DangerBrush",
            _ => "MutedBrush",
        });
    }

    private void UpdateEmpty() =>
        EmptyState.Visibility = _mods.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void UpdateCounts()
    {
        var on = _mods.Count(m => m.Enabled);
        CountText.Text = _mods.Count == 0 ? "" : Loc.T("S_Count", _mods.Count, Loc.Plural(_mods.Count, "mod"), on);
    }

    // ---------- Окно ----------

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_StateChanged(object? sender, EventArgs e) =>
        MaxGlyph.Text = WindowState == WindowState.Maximized ? "" : "";

    // ---------- Шапка: игра / BepInEx ----------

    private void RefreshHeader()
    {
        var dir = _lib.Settings.GameDir;
        var ok = GameLocator.IsGameDir(dir);

        GamePathText.Text = ok ? dir : Loc.T("Game_PathPrompt");
        GameDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, ok ? "OkBrush" : "DangerBrush");
        GameChipText.Text = Loc.T(ok ? "Game_Found" : "Game_NotFound");

        var bep = ok && BepInExInstaller.IsInstalled(dir!);
        if (!ok)
        {
            BepDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "MutedBrush");
            BepChipText.Text = "BepInEx";
        }
        else if (bep)
        {
            BepDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "OkBrush");
            BepChipText.Text = Loc.T("Bep_Installed");
        }
        else
        {
            BepDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "WarnBrush");
            BepChipText.Text = Loc.T("Bep_Install");
        }

        if (ok && GameLocator.HasNonAsciiPath(dir!))
        {
            WarningText.Text = Loc.T("W_NonAscii");
            WarningText.Visibility = Visibility.Visible;
        }
        else WarningText.Visibility = Visibility.Collapsed;
    }

    private void PickGame_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("Dlg_PickGameTitle") };
        if (dlg.ShowDialog() != true) return;

        if (!GameLocator.IsGameDir(dlg.FolderName))
        {
            SetStatus(Loc.T("S_NoExeInFolder", GameLocator.ExeName), StatusKind.Error);
            return;
        }
        _lib.Settings.GameDir = dlg.FolderName;
        _lib.Save();
        RefreshHeader();
        SetStatus(Loc.T("S_GameFolderUpdated"), StatusKind.Ok);
        ScanInstalled(announce: false);
    }

    private async void InstallBep_Click(object sender, RoutedEventArgs e)
    {
        var dir = _lib.Settings.GameDir;
        if (!GameLocator.IsGameDir(dir))
        {
            SetStatus(Loc.T("S_PickGameFirst"), StatusKind.Error);
            return;
        }
        if (BepInExInstaller.IsInstalled(dir!))
        {
            SetStatus(Loc.T("S_BepAlready"), StatusKind.Ok);
            return;
        }

        SetStatus(Loc.T("S_BepDownloading"));
        try
        {
            await BepInExInstaller.InstallAsync(dir!);
            SetStatus(Loc.T("S_BepInstalled"), StatusKind.Ok);
            try { _lib.Reapply(); } catch (Exception ex) { SetStatus(Loc.T("S_Error", ex.Message), StatusKind.Error); }
        }
        catch (Exception ex)
        {
            SetStatus(Loc.T("S_BepFailed", ex.Message), StatusKind.Error);
        }
        RefreshHeader();
    }

    private void OpenGameFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = _lib.Settings.GameDir;
        if (!GameLocator.IsGameDir(dir))
        {
            SetStatus(Loc.T("S_NoGameFolder"), StatusKind.Error);
            return;
        }
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
    }

    // ---------- Поиск ----------

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => _view?.Refresh();

    // ---------- Добавление модов ----------

    private void AddMod_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("Dlg_PickModsTitle"),
            Filter = Loc.T("Dlg_ModsFilter"),
            Multiselect = true,
        };
        if (dlg.ShowDialog() == true) AddFiles(dlg.FileNames);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropOverlay.Visibility = Visibility.Visible;
        }
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        var p = e.GetPosition(this);
        if (p.X <= 0 || p.Y <= 0 || p.X >= ActualWidth || p.Y >= ActualHeight)
            DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files);
    }

    private void AddFiles(IEnumerable<string> files)
    {
        var errors = new List<string>();
        var added = 0;
        foreach (var f in files)
        {
            var before = _lib.Settings.Mods.Count;
            try
            {
                if (Path.GetExtension(f).Equals(ModLibrary.PackExtension, StringComparison.OrdinalIgnoreCase))
                {
                    var imported = _lib.ImportPack(f);
                    foreach (var m in imported) Attach(m);
                    added += imported.Count;
                }
                else
                {
                    Attach(_lib.Add(f));
                    added++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(f)}: {ex.Message}");
                // мод мог попасть в библиотеку, но не поставиться — показываем его выключенным с причиной
                if (_lib.Settings.Mods.Count > before)
                {
                    var m = _lib.Settings.Mods[^1];
                    m.Status = ex.Message;
                    Attach(m);
                }
            }
        }

        if (errors.Count == 0) SetStatus(Loc.T("S_Added", added), StatusKind.Ok);
        else SetStatus(string.Join("; ", errors), StatusKind.Error);
        RefreshHeader();
    }

    // ---------- Управление модами ----------

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.Tag is not ModEntry mod) return;
        var want = cb.IsChecked == true;
        try
        {
            _lib.SetEnabled(mod, want);
            mod.Status = "";
            SetStatus(Loc.T(want ? "S_ModEnabled" : "S_ModDisabled", Loc.T("Quoted", mod.Name)), StatusKind.Ok);
        }
        catch (Exception ex)
        {
            cb.IsChecked = mod.Enabled; // откатываем переключатель
            mod.Status = ex.Message;
            SetStatus(ex.Message, StatusKind.Error);
        }
    }

    private void RemoveOne_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ModEntry mod }) RemoveMods(new[] { mod });
    }

    private void ModsList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Delete) return;
        RemoveMods(ModsList.SelectedItems.Cast<ModEntry>().ToList());
        e.Handled = true;
    }

    private void RemoveMods(IList<ModEntry> selected)
    {
        if (selected.Count == 0) return;
        var what = selected.Count == 1 ? Loc.T("Quoted", selected[0].Name) : Loc.T("S_CountPlain", selected.Count, Loc.Plural(selected.Count, "mod"));
        if (MessageBox.Show(Loc.T("Dlg_RemoveConfirm", what), Loc.T("Dlg_Confirm"),
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        foreach (var m in selected)
        {
            _lib.Remove(m);
            _mods.Remove(m);
        }
        RefreshUpdateUi();
        NexusPane.RefreshInstalled();
        SetStatus(Loc.T("S_Removed", selected.Count), StatusKind.Ok);
    }

    // ---------- Сборки для друзей ----------

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var mods = ModsList.SelectedItems.Count > 0
            ? ModsList.SelectedItems.Cast<ModEntry>().ToList()
            : _mods.ToList();
        if (mods.Count == 0) { SetStatus(Loc.T("S_NothingToExport"), StatusKind.Error); return; }

        var dlg = new SaveFileDialog
        {
            Title = Loc.T("Dlg_SavePackTitle"),
            Filter = Loc.T("Dlg_PackFilter", ModLibrary.PackExtension),
            FileName = "my-modpack" + ModLibrary.PackExtension,
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            _lib.ExportPack(dlg.FileName, mods);
            SetStatus(Loc.T("S_PackSaved", mods.Count, Loc.Plural(mods.Count, "mod")), StatusKind.Ok);
        }
        catch (Exception ex) { SetStatus(Loc.T("S_ExportFailed", ex.Message), StatusKind.Error); }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("Dlg_OpenPackTitle"),
            Filter = Loc.T("Dlg_PackFilter", ModLibrary.PackExtension),
        };
        if (dlg.ShowDialog() == true) AddFiles(new[] { dlg.FileName });
    }

    // ---------- Обновления и моды по ссылке ----------

    private bool _busy;

    private static string? ValidateLink(string text)
    {
        try { UpdateService.ParseLink(text); return null; }
        catch (Exception ex) { return ex.Message; }
    }

    private void RefreshUpdateUi()
    {
        var n = _mods.Count(m => m.UpdateAvailable && !NeedsBrowser(m));
        UpdateAllButton.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateAllButton.Content = Loc.T("Btn_UpdateAllN", n);
    }

    private async Task CheckAllAsync(bool announce)
    {
        if (_busy) return;

        var withSource = _mods.Where(m => m.Source != null).ToList();
        if (withSource.Count == 0)
        {
            if (announce) SetStatus(Loc.T("S_NoSources"));
            return;
        }

        _busy = true;
        CheckButton.IsEnabled = false;
        if (announce) SetStatus(Loc.T("S_CheckingUpdates"));
        try
        {
            var (available, failed) = await _lib.CheckUpdatesAsync(withSource);
            RefreshUpdateUi();

            if (available > 0) SetStatus(Loc.T("S_UpdatesAvailable", available), StatusKind.Ok);
            else if (failed > 0) SetStatus(Loc.T("S_SomeCheckFailed"), StatusKind.Error);
            else if (announce) SetStatus(Loc.T("S_AllUpToDate"), StatusKind.Ok);
        }
        catch (Exception ex)
        {
            SetStatus(Loc.T("S_UpdateCheckError", ex.Message), StatusKind.Error);
        }
        finally
        {
            _busy = false;
            CheckButton.IsEnabled = true;
        }
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await CheckAllAsync(announce: true);

    private async Task ApplyUpdateAsync(ModEntry mod)
    {
        if (mod.Pending == null) return;
        mod.Status = "";
        try
        {
            if (NeedsBrowser(mod))
            {
                // бесплатный аккаунт Nexus: файл можно взять только через сайт, дальше сработает nxm-ссылка
                await OpenNexusDownloadPageAsync(mod.Source!.NexusModId, mod.Pending.NexusFileId, mod.Name);
                return;
            }
            SetStatus(Loc.T("S_Updating", Loc.T("Quoted", mod.Name)));
            await _lib.UpdateAsync(mod);
            SetStatus(Loc.T("S_Updated", Loc.T("Quoted", mod.Name), mod.Source?.Version ?? ""), StatusKind.Ok);
        }
        catch (Exception ex)
        {
            mod.Status = Loc.T("S_ModUpdateFailed", ex.Message);
            SetStatus(Loc.T("S_UpdateFailedShort", Loc.T("Quoted", mod.Name)), StatusKind.Error);
        }
        RefreshUpdateUi();
    }

    private async void UpdateOne_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || sender is not Button { Tag: ModEntry mod }) return;
        _busy = true;
        try { await ApplyUpdateAsync(mod); }
        finally { _busy = false; }
    }

    private async void UpdateAll_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var pending = _mods.Where(m => m.UpdateAvailable && !NeedsBrowser(m)).ToList();
            foreach (var m in pending) await ApplyUpdateAsync(m);

            var left = pending.Count(m => m.UpdateAvailable);
            SetStatus(left == 0 ? Loc.T("S_UpdatedCount", pending.Count) : Loc.T("S_UpdatedPartial", pending.Count - left, pending.Count),
                left == 0 ? StatusKind.Ok : StatusKind.Error);
        }
        finally { _busy = false; }
    }

    private async void AddByLink_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new LinkDialog(
            Loc.T("Dlg_AddLinkTitle"),
            Loc.T("Dlg_AddLinkHint"),
            "", ValidateLink) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        // страница мода на Nexus — отдельный путь: там нужен API-ключ, а бесплатному аккаунту ещё и поход на сайт
        if (UpdateService.ParseLink(dlg.Url) is { Type: "nexus" } nx)
        {
            await InstallNexusModAsync(new NexusMod { ModId = nx.NexusModId });
            return;
        }

        var before = _lib.Settings.Mods.Count;
        SetStatus(Loc.T("S_DownloadingMod"));
        try
        {
            var mod = await _lib.AddFromLinkAsync(dlg.Url);
            Attach(mod);
            SetStatus(Loc.T("S_AddedMod", Loc.T("Quoted", mod.Name)), StatusKind.Ok);
        }
        catch (Exception ex)
        {
            // мод мог скачаться, но не встать в игру — показываем его в списке с причиной
            if (_lib.Settings.Mods.Count > before)
            {
                var m = _lib.Settings.Mods[^1];
                m.Status = ex.Message;
                Attach(m);
            }
            SetStatus(ex.Message, StatusKind.Error);
        }
        RefreshHeader();
    }

    private async void LinkSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ModEntry mod }) return;

        var dlg = new LinkDialog(
            Loc.T("Dlg_SourceTitle", mod.Name),
            Loc.T("Dlg_SourceHint"),
            mod.Source?.Url ?? "",
            s => string.IsNullOrWhiteSpace(s) ? null : ValidateLink(s)) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        if (string.IsNullOrWhiteSpace(dlg.Url))
        {
            _lib.SetSource(mod, null);
            RefreshUpdateUi();
            SetStatus(Loc.T("S_SourceRemoved", Loc.T("Quoted", mod.Name)));
            return;
        }

        _lib.SetSource(mod, UpdateService.ParseLink(dlg.Url));
        await CheckAllAsync(announce: true);
    }

    // ---------- Вкладки, настройки, язык ----------

    private void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (ModsPanel == null || NexusPane == null || ActionsPanel == null) return; // события во время загрузки XAML
        var nexus = TabNexus.IsChecked == true;
        ModsPanel.Visibility = nexus ? Visibility.Collapsed : Visibility.Visible;
        ActionsPanel.Visibility = nexus ? Visibility.Collapsed : Visibility.Visible;
        NexusPane.Visibility = nexus ? Visibility.Visible : Visibility.Collapsed;
        if (nexus) _ = NexusPane.EnsureLoadedAsync();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) =>
        new SettingsDialog(this, _lib) { Owner = this }.ShowDialog();

    /// <summary>Тему сменили в настройках: применяем сразу и запоминаем.</summary>
    internal void SetTheme(string mode)
    {
        _lib.Settings.Theme = Theme.Normalize(mode);
        _lib.Save();
        Theme.Apply(_lib.Settings.Theme);
    }

    // ---------- Обновление самого менеджера ----------

    private SelfUpdate? _selfUpdate;
    private bool _selfUpdating;

    internal SelfUpdate? PendingSelfUpdate => _selfUpdate;

    /// <summary>Ищет новую версию менеджера. Ошибка возвращается текстом (при тихой проверке на старте её не показываем).</summary>
    internal async Task<(SelfUpdate? Update, string? Error)> CheckSelfUpdateAsync(bool announce)
    {
        try
        {
            if (announce) SetStatus(Loc.T("Upd_Checking"));
            _selfUpdate = await SelfUpdater.CheckAsync();
            RefreshSelfUpdateUi();
            if (_selfUpdate != null)
                SetStatus(Loc.T("Upd_Available", _selfUpdate.Version.ToString(3)), StatusKind.Ok);
            else if (announce)
                SetStatus(Loc.T("Upd_UpToDate"), StatusKind.Ok);
            return (_selfUpdate, null);
        }
        catch (Exception ex)
        {
            if (announce) SetStatus(Loc.T("Upd_Failed", ex.Message), StatusKind.Error);
            return (null, ex.Message);
        }
    }

    private void RefreshSelfUpdateUi()
    {
        var show = _selfUpdate != null && SelfUpdater.CanSelfUpdate;
        SelfUpdateButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show) SelfUpdateButton.Content = Loc.T("Upd_Button", _selfUpdate!.Version.ToString(3));
    }

    private async void SelfUpdate_Click(object sender, RoutedEventArgs e) => await InstallSelfUpdateAsync();

    /// <summary>Скачивает новую версию, подменяет файлы и перезапускает менеджер.</summary>
    internal async Task InstallSelfUpdateAsync()
    {
        var u = _selfUpdate;
        if (u == null || _selfUpdating) return;

        var ask = MessageBox.Show(this, Loc.T("Upd_Confirm", u.Version.ToString(3)), Loc.T("Upd_ConfirmTitle"),
            MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (ask != MessageBoxResult.OK) return;

        _selfUpdating = true;
        SelfUpdateButton.IsEnabled = false;
        try
        {
            var shown = -1;
            var progress = new Progress<double>(p =>
            {
                var pct = (int)(p * 100);
                if (pct == shown) return;
                shown = pct;
                SetStatus(Loc.T("Upd_Downloading", u.Version.ToString(3), pct));
            });
            SetStatus(Loc.T("Upd_Downloading", u.Version.ToString(3), 0));
            await SelfUpdater.DownloadAndApplyAsync(u, progress);

            SetStatus(Loc.T("Upd_Restarting"), StatusKind.Ok);
            _lib.Save();
            SelfUpdater.Restart();
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            SetStatus(Loc.T("Upd_Error", ex.Message), StatusKind.Error);
            SelfUpdateButton.IsEnabled = true;
        }
        finally
        {
            _selfUpdating = false;
        }
    }

    /// <summary>Язык сменили в настройках: сохраняем и обновляем тексты, которые собираются в коде.</summary>
    private void OnLanguageChanged()
    {
        _lib.Settings.Language = Loc.Lang;
        _lib.Save();

        foreach (var m in _mods)
        {
            m.RefreshLocalized();
            _lib.RefreshInfo(m);
        }
        RefreshHeader();
        UpdateCounts();
        RefreshUpdateUi();
        RefreshSelfUpdateUi();
        NexusPane.OnLanguageChanged();
        SetStatus(Loc.T("Status_Ready"));
    }

    /// <summary>Показать сообщение в строке состояния (для вкладки Nexus).</summary>
    internal void ReportStatus(string text, bool error = false) =>
        SetStatus(text, error ? StatusKind.Error : StatusKind.Info);

    // ---------- Nexus Mods ----------

    /// <summary>Бесплатному аккаунту Nexus прямую ссылку на файл не дают — скачивание идёт через сайт и nxm://.</summary>
    private bool NeedsBrowser(ModEntry m) => m.Source?.Type == "nexus" && !_lib.Settings.NexusPremium;

    /// <summary>Сообщение от повторного запуска программы (клик по nxm-ссылке или просто второй запуск).</summary>
    public void HandleExternal(string message)
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        if (NexusService.IsNxm(message)) _ = HandleNxmAsync(message);
    }

    internal async Task<bool> EnsureNexusKeyAsync() => NexusService.HasKey || await PromptNexusKeyAsync();

    internal async Task<bool> PromptNexusKeyAsync()
    {
        var dlg = new LinkDialog(
            Loc.T("Dlg_KeyTitle"),
            Loc.T("Dlg_KeyHint"),
            "", s => string.IsNullOrWhiteSpace(s) ? Loc.T("Dlg_KeyEmpty") : null) { Owner = this };
        if (dlg.ShowDialog() != true) return false;
        return await ApplyNexusKeyAsync(dlg.Url);
    }

    private async Task<bool> ApplyNexusKeyAsync(string key)
    {
        SetStatus(Loc.T("S_CheckingKey"));
        NexusService.ApiKey = key;
        try
        {
            var user = await NexusService.ValidateAsync();
            _lib.SetNexusKey(key, user.Name, user.IsPremium);
            SetStatus(Loc.T("S_NexusAs", user.Name, Loc.T(user.IsPremium ? "Nx_Premium" : "Nx_FreeAccount")), StatusKind.Ok);
            NexusPane.RefreshAccount();
            return true;
        }
        catch (Exception ex)
        {
            NexusService.ApiKey = _lib.Settings.NexusApiKey; // возвращаем прежний ключ
            SetStatus(ex.Message, StatusKind.Error);
            return false;
        }
    }

    private static bool NxmOn()
    {
        try { return NexusService.IsNxmHandlerOurs(); }
        catch { return false; }
    }

    internal void ToggleNxmHandler()
    {
        try
        {
            if (NxmOn())
            {
                NexusService.UnregisterNxmHandler(_lib.Settings.NxmPreviousHandler);
                _lib.Settings.NxmPreviousHandler = null;
                _lib.Save();
                SetStatus(Loc.T("S_NxmOff"));
            }
            else
            {
                var previous = NexusService.RegisterNxmHandler();
                if (previous != null) _lib.Settings.NxmPreviousHandler = previous;
                _lib.Save();
                SetStatus(Loc.T("S_NxmOn"), StatusKind.Ok);
            }
        }
        catch (Exception ex)
        {
            SetStatus(Loc.T("S_NxmFailed", ex.Message), StatusKind.Error);
        }
    }

    /// <summary>Открывает на сайте страницу скачивания файла: оттуда nxm-ссылка вернётся в менеджер.</summary>
    private async Task OpenNexusDownloadPageAsync(int modId, long? fileId, string name)
    {
        var id = fileId;
        if (id is null or 0)
        {
            var main = NexusService.PickMainFile(await NexusService.GetFilesAsync(modId))
                       ?? throw new InvalidOperationException(Loc.T("E_NexusNoFiles"));
            id = main.FileId;
        }

        Process.Start(new ProcessStartInfo(NexusService.FileDownloadPageUrl(modId, id.Value)) { UseShellExecute = true });

        var label = string.IsNullOrWhiteSpace(name) ? Loc.T("Nx_ModN", modId) : Loc.T("Quoted", name);
        if (NxmOn())
            SetStatus(Loc.T("S_NexusClickSlow", label));
        else
            SetStatus(Loc.T("S_NexusEnableNxm", label),
                StatusKind.Error);
    }

    /// <summary>Кнопка «Установить» в каталоге. true — мод уже стоит; false — отказ, ошибка или нужен поход на сайт.</summary>
    internal async Task<bool> InstallNexusModAsync(NexusMod item)
    {
        if (_busy)
        {
            SetStatus(Loc.T("S_Busy"));
            return false;
        }
        if (!await EnsureNexusKeyAsync()) return false;

        var name = string.IsNullOrWhiteSpace(item.Name) ? Loc.T("Nx_ModN", item.ModId) : Loc.T("Quoted", item.Name);
        var before = _lib.Settings.Mods.Count;
        _busy = true;
        try
        {
            if (_lib.FindNexusMod(item.ModId) != null)
            {
                SetStatus(Loc.T("S_NexusAlready", name));
                return true;
            }

            if (!_lib.Settings.NexusPremium)
            {
                await OpenNexusDownloadPageAsync(item.ModId, null, item.Name);
                return false;
            }

            SetStatus(Loc.T("S_NexusDownloading", name));
            var (mod, updated) = await _lib.InstallFromNexusAsync(item.ModId, null, null, null, item.Name);
            FinishNexusInstall(mod, updated);
            return true;
        }
        catch (Exception ex)
        {
            ReportInstallError(ex, before);
            return false;
        }
        finally
        {
            _busy = false;
            RefreshHeader();
        }
    }

    /// <summary>Пришла nxm-ссылка (клик «Slow download» / «Mod manager download» на сайте).</summary>
    private async Task HandleNxmAsync(string link)
    {
        NxmLink nxm;
        try { nxm = NexusService.ParseNxm(link); }
        catch (Exception ex)
        {
            SetStatus(ex.Message, StatusKind.Error);
            return;
        }

        while (_busy) await Task.Delay(300); // например, ещё идёт проверка обновлений
        if (!await EnsureNexusKeyAsync()) return;

        var before = _lib.Settings.Mods.Count;
        _busy = true;
        try
        {
            SetStatus(Loc.T("S_NexusDownloadingAny"));
            var (mod, updated) = await _lib.InstallFromNexusAsync(nxm.ModId, nxm.FileId, nxm.Key, nxm.Expires);
            FinishNexusInstall(mod, updated);
        }
        catch (Exception ex)
        {
            ReportInstallError(ex, before);
        }
        finally
        {
            _busy = false;
            RefreshHeader();
        }
    }

    private void FinishNexusInstall(ModEntry mod, bool updated)
    {
        if (!updated) Attach(mod);
        RefreshUpdateUi();
        NexusPane.RefreshInstalled();
        SetStatus(updated
            ? Loc.T("S_Updated", Loc.T("Quoted", mod.Name), mod.Source?.Version ?? "")
            : Loc.T("S_NexusInstalled", Loc.T("Quoted", mod.Name)), StatusKind.Ok);
    }

    /// <summary>Мод мог скачаться, но не встать в игру — тогда показываем его в списке с причиной.</summary>
    private void ReportInstallError(Exception ex, int modsBefore)
    {
        if (_lib.Settings.Mods.Count > modsBefore)
        {
            var m = _lib.Settings.Mods[^1];
            m.Status = ex.Message;
            Attach(m);
            NexusPane.RefreshInstalled();
        }
        SetStatus(ex.Message, StatusKind.Error);
    }

    // ---------- Запуск ----------

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = _lib.Settings.GameDir;
            if (GameLocator.IsGameDir(dir))
                Process.Start(new ProcessStartInfo(Path.Combine(dir!, GameLocator.ExeName)) { WorkingDirectory = dir, UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo($"steam://run/{GameLocator.SteamAppId}") { UseShellExecute = true });
            SetStatus(Loc.T("S_Launching"), StatusKind.Ok);
        }
        catch (Exception ex) { SetStatus(Loc.T("S_LaunchFailed", ex.Message), StatusKind.Error); }
    }
}
