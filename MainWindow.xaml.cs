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
        InitializeComponent();
        _lib.Load();

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
            SetStatus("Игра не найдена — нажми на красную плашку и укажи папку", StatusKind.Error);
        else if (!BepInExInstaller.IsInstalled(dir!))
            SetStatus("Сначала установи BepInEx — нажми на жёлтую плашку", StatusKind.Info);

        // тихая проверка обновлений при запуске
        Loaded += async (_, _) =>
        {
            // программу могли запустить кликом по nxm-ссылке на сайте Nexus
            if (App.StartupNxm is { } nxm) await HandleNxmAsync(nxm);
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
                SetStatus($"Найдено в папке игры: {found.Count} {Plural(found.Count, "мод", "мода", "модов")}", StatusKind.Ok);
            else if (announce)
                SetStatus("Новых модов в папке игры не найдено");
        }
        catch (Exception ex)
        {
            SetStatus("Не удалось просканировать папку игры: " + ex.Message, StatusKind.Error);
        }
    }

    private void Rescan_Click(object sender, RoutedEventArgs e)
    {
        if (!GameLocator.IsGameDir(_lib.Settings.GameDir))
        {
            SetStatus("Сначала укажи папку игры", StatusKind.Error);
            return;
        }
        ScanInstalled(announce: true);
    }

    private void SetStatus(string text, StatusKind kind = StatusKind.Info)
    {
        StatusText.Text = text;
        StatusDot.Fill = Res(kind switch
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
        CountText.Text = _mods.Count == 0 ? "" : $"{_mods.Count} {Plural(_mods.Count, "мод", "мода", "модов")} · включено {on}";
    }

    private static string Plural(int n, string one, string few, string many)
    {
        n = Math.Abs(n) % 100;
        var d = n % 10;
        if (n is > 10 and < 20) return many;
        if (d == 1) return one;
        if (d is >= 2 and <= 4) return few;
        return many;
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

        GamePathText.Text = ok ? dir : "Укажи папку с CasualtiesUnknown.exe";
        GameDot.Fill = Res(ok ? "OkBrush" : "DangerBrush");
        GameChipText.Text = ok ? "Игра найдена" : "Игра не найдена";

        var bep = ok && BepInExInstaller.IsInstalled(dir!);
        if (!ok)
        {
            BepDot.Fill = Res("MutedBrush");
            BepChipText.Text = "BepInEx";
        }
        else if (bep)
        {
            BepDot.Fill = Res("OkBrush");
            BepChipText.Text = "BepInEx установлен";
        }
        else
        {
            BepDot.Fill = Res("WarnBrush");
            BepChipText.Text = "Установить BepInEx";
        }

        if (ok && GameLocator.HasNonAsciiPath(dir!))
        {
            WarningText.Text = "В пути к игре есть не-ASCII символы: BepInEx может не загрузить моды. Если не работает — перенеси игру.";
            WarningText.Visibility = Visibility.Visible;
        }
        else WarningText.Visibility = Visibility.Collapsed;
    }

    private void PickGame_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Папка с CasualtiesUnknown.exe" };
        if (dlg.ShowDialog() != true) return;

        if (!GameLocator.IsGameDir(dlg.FolderName))
        {
            SetStatus("В этой папке нет " + GameLocator.ExeName, StatusKind.Error);
            return;
        }
        _lib.Settings.GameDir = dlg.FolderName;
        _lib.Save();
        RefreshHeader();
        SetStatus("Папка игры обновлена", StatusKind.Ok);
        ScanInstalled(announce: false);
    }

    private async void InstallBep_Click(object sender, RoutedEventArgs e)
    {
        var dir = _lib.Settings.GameDir;
        if (!GameLocator.IsGameDir(dir))
        {
            SetStatus("Сначала укажи папку игры", StatusKind.Error);
            return;
        }
        if (BepInExInstaller.IsInstalled(dir!))
        {
            SetStatus("BepInEx уже установлен", StatusKind.Ok);
            return;
        }

        SetStatus("Скачиваю BepInEx…");
        try
        {
            await BepInExInstaller.InstallAsync(dir!);
            SetStatus("BepInEx установлен. Запусти игру один раз, чтобы он создал конфиги.", StatusKind.Ok);
            try { _lib.Reapply(); } catch (Exception ex) { SetStatus("Ошибка: " + ex.Message, StatusKind.Error); }
        }
        catch (Exception ex)
        {
            SetStatus("Не удалось скачать BepInEx: " + ex.Message, StatusKind.Error);
        }
        RefreshHeader();
    }

    private void OpenGameFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = _lib.Settings.GameDir;
        if (!GameLocator.IsGameDir(dir))
        {
            SetStatus("Папка игры не выбрана", StatusKind.Error);
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
            Title = "Выбери моды",
            Filter = "Моды и сборки (*.dll;*.zip;*.cumods)|*.dll;*.zip;*.cumods",
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

        if (errors.Count == 0) SetStatus($"Добавлено: {added}", StatusKind.Ok);
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
            SetStatus(want ? $"«{mod.Name}» включён" : $"«{mod.Name}» выключен", StatusKind.Ok);
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
        var what = selected.Count == 1 ? $"«{selected[0].Name}»" : $"{selected.Count} {Plural(selected.Count, "мод", "мода", "модов")}";
        if (MessageBox.Show($"Удалить {what}? Файлы будут убраны из игры.", "Подтверждение",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        foreach (var m in selected)
        {
            _lib.Remove(m);
            _mods.Remove(m);
        }
        RefreshUpdateUi();
        _nexus?.RefreshInstalled();
        SetStatus($"Удалено: {selected.Count}", StatusKind.Ok);
    }

    // ---------- Сборки для друзей ----------

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var mods = ModsList.SelectedItems.Count > 0
            ? ModsList.SelectedItems.Cast<ModEntry>().ToList()
            : _mods.ToList();
        if (mods.Count == 0) { SetStatus("Нечего экспортировать — добавь хотя бы один мод", StatusKind.Error); return; }

        var dlg = new SaveFileDialog
        {
            Title = "Сохранить сборку",
            Filter = $"Сборка модов (*{ModLibrary.PackExtension})|*{ModLibrary.PackExtension}",
            FileName = "my-modpack" + ModLibrary.PackExtension,
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            _lib.ExportPack(dlg.FileName, mods);
            SetStatus($"Сборка сохранена ({mods.Count} {Plural(mods.Count, "мод", "мода", "модов")}). Отправь файл друзьям.", StatusKind.Ok);
        }
        catch (Exception ex) { SetStatus("Ошибка экспорта: " + ex.Message, StatusKind.Error); }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Открыть сборку",
            Filter = $"Сборка модов (*{ModLibrary.PackExtension})|*{ModLibrary.PackExtension}",
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
        UpdateAllButton.Content = $"Обновить все ({n})";
    }

    private async Task CheckAllAsync(bool announce)
    {
        if (_busy) return;

        var withSource = _mods.Where(m => m.Source != null).ToList();
        if (withSource.Count == 0)
        {
            if (announce) SetStatus("Ни у одного мода не задан источник — нажми значок ссылки на карточке или добавь мод кнопкой «По ссылке»");
            return;
        }

        _busy = true;
        CheckButton.IsEnabled = false;
        if (announce) SetStatus("Проверяю обновления…");
        try
        {
            var (available, failed) = await _lib.CheckUpdatesAsync(withSource);
            RefreshUpdateUi();

            if (available > 0) SetStatus($"Доступны обновления: {available}", StatusKind.Ok);
            else if (failed > 0) SetStatus("Часть модов проверить не удалось — подробности на карточках", StatusKind.Error);
            else if (announce) SetStatus("Все моды актуальны", StatusKind.Ok);
        }
        catch (Exception ex)
        {
            SetStatus("Ошибка проверки обновлений: " + ex.Message, StatusKind.Error);
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
            SetStatus($"Обновляю «{mod.Name}»…");
            await _lib.UpdateAsync(mod);
            SetStatus($"«{mod.Name}» обновлён до {mod.Source?.Version}", StatusKind.Ok);
        }
        catch (Exception ex)
        {
            mod.Status = "Не удалось обновить: " + ex.Message;
            SetStatus($"«{mod.Name}»: не удалось обновить", StatusKind.Error);
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
            SetStatus(left == 0 ? $"Обновлено модов: {pending.Count}" : $"Обновлено: {pending.Count - left} из {pending.Count}",
                left == 0 ? StatusKind.Ok : StatusKind.Error);
        }
        finally { _busy = false; }
    }

    private async void AddByLink_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new LinkDialog(
            "Добавить мод по ссылке",
            "Ссылка на репозиторий GitHub (возьмём последний релиз), страницу мода на Nexus Mods или прямая ссылка на .dll / .zip. " +
            "Менеджер запомнит источник и будет сам находить обновления.",
            "", ValidateLink) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        // страница мода на Nexus — отдельный путь: там нужен API-ключ, а бесплатному аккаунту ещё и поход на сайт
        if (UpdateService.ParseLink(dlg.Url) is { Type: "nexus" } nx)
        {
            await InstallNexusModAsync(new NexusMod { ModId = nx.NexusModId });
            return;
        }

        var before = _lib.Settings.Mods.Count;
        SetStatus("Скачиваю мод…");
        try
        {
            var mod = await _lib.AddFromLinkAsync(dlg.Url);
            Attach(mod);
            SetStatus($"Добавлено: «{mod.Name}»", StatusKind.Ok);
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
            $"Источник обновлений: {mod.Name}",
            "Ссылка на репозиторий GitHub, страницу мода на Nexus Mods или прямая ссылка на .dll / .zip. Оставь поле пустым, чтобы убрать источник. " +
            "Версия уже установленного мода неизвестна, поэтому менеджер сразу предложит обновиться до последней.",
            mod.Source?.Url ?? "",
            s => string.IsNullOrWhiteSpace(s) ? null : ValidateLink(s)) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        if (string.IsNullOrWhiteSpace(dlg.Url))
        {
            _lib.SetSource(mod, null);
            RefreshUpdateUi();
            SetStatus($"Источник у «{mod.Name}» убран");
            return;
        }

        _lib.SetSource(mod, UpdateService.ParseLink(dlg.Url));
        await CheckAllAsync(announce: true);
    }

    // ---------- Nexus Mods ----------

    private NexusWindow? _nexus;

    private void Nexus_Click(object sender, RoutedEventArgs e)
    {
        if (_nexus != null)
        {
            _nexus.Activate();
            return;
        }
        _nexus = new NexusWindow(this, _lib) { Owner = this };
        _nexus.Closed += (_, _) => _nexus = null;
        _nexus.Show();
    }

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
            "API-ключ Nexus Mods",
            "Личный ключ: nexusmods.com → иконка профиля → Site preferences → вкладка API Keys → «Personal API Key». " +
            "Ключ хранится только на этом компьютере и в сборки (.cumods) не попадает.",
            "", s => string.IsNullOrWhiteSpace(s) ? "Вставь ключ" : null) { Owner = this };
        if (dlg.ShowDialog() != true) return false;
        return await ApplyNexusKeyAsync(dlg.Url);
    }

    private async Task<bool> ApplyNexusKeyAsync(string key)
    {
        SetStatus("Проверяю ключ Nexus…");
        NexusService.ApiKey = key;
        try
        {
            var user = await NexusService.ValidateAsync();
            _lib.SetNexusKey(key, user.Name, user.IsPremium);
            SetStatus($"Nexus: {user.Name} ({(user.IsPremium ? "Premium" : "бесплатный аккаунт")})", StatusKind.Ok);
            _nexus?.RefreshAccount();
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
                SetStatus("Приём nxm-ссылок выключен");
            }
            else
            {
                var previous = NexusService.RegisterNxmHandler();
                if (previous != null) _lib.Settings.NxmPreviousHandler = previous;
                _lib.Save();
                SetStatus("Приём nxm-ссылок включён: кнопки скачивания на Nexus теперь открывают этот менеджер", StatusKind.Ok);
            }
        }
        catch (Exception ex)
        {
            SetStatus("Не удалось изменить обработчик nxm: " + ex.Message, StatusKind.Error);
        }
    }

    /// <summary>Открывает на сайте страницу скачивания файла: оттуда nxm-ссылка вернётся в менеджер.</summary>
    private async Task OpenNexusDownloadPageAsync(int modId, long? fileId, string name)
    {
        var id = fileId;
        if (id is null or 0)
        {
            var main = NexusService.PickMainFile(await NexusService.GetFilesAsync(modId))
                       ?? throw new InvalidOperationException("У мода на Nexus нет файлов");
            id = main.FileId;
        }

        Process.Start(new ProcessStartInfo(NexusService.FileDownloadPageUrl(modId, id.Value)) { UseShellExecute = true });

        var label = string.IsNullOrWhiteSpace(name) ? $"Мод #{modId}" : $"«{name}»";
        if (NxmOn())
            SetStatus($"{label}: на открывшейся странице нажми «Slow download» — менеджер скачает и поставит мод сам");
        else
            SetStatus($"{label}: включи «Приём nxm-ссылок» в окне Nexus, иначе менеджер не получит файл. Или скачай .zip и перетащи в окно.",
                StatusKind.Error);
    }

    /// <summary>Кнопка «Установить» в каталоге. true — мод уже стоит; false — отказ, ошибка или нужен поход на сайт.</summary>
    internal async Task<bool> InstallNexusModAsync(NexusMod item)
    {
        if (_busy)
        {
            SetStatus("Подожди, пока закончится текущая операция");
            return false;
        }
        if (!await EnsureNexusKeyAsync()) return false;

        var name = string.IsNullOrWhiteSpace(item.Name) ? $"мод #{item.ModId}" : $"«{item.Name}»";
        var before = _lib.Settings.Mods.Count;
        _busy = true;
        try
        {
            if (_lib.FindNexusMod(item.ModId) != null)
            {
                SetStatus($"{name} уже есть в списке — обновления ищи кнопкой «Обновления»");
                return true;
            }

            if (!_lib.Settings.NexusPremium)
            {
                await OpenNexusDownloadPageAsync(item.ModId, null, item.Name);
                return false;
            }

            SetStatus($"Скачиваю {name} с Nexus…");
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
            SetStatus("Скачиваю мод с Nexus…");
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
        _nexus?.RefreshInstalled();
        SetStatus(updated
            ? $"«{mod.Name}» обновлён до {mod.Source?.Version}"
            : $"Установлено с Nexus: «{mod.Name}»", StatusKind.Ok);
    }

    /// <summary>Мод мог скачаться, но не встать в игру — тогда показываем его в списке с причиной.</summary>
    private void ReportInstallError(Exception ex, int modsBefore)
    {
        if (_lib.Settings.Mods.Count > modsBefore)
        {
            var m = _lib.Settings.Mods[^1];
            m.Status = ex.Message;
            Attach(m);
            _nexus?.RefreshInstalled();
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
            SetStatus("Запускаю игру…", StatusKind.Ok);
        }
        catch (Exception ex) { SetStatus("Не удалось запустить: " + ex.Message, StatusKind.Error); }
    }
}
