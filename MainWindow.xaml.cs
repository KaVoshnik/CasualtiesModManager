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
        UpdateEmpty();
        UpdateCounts();

        var dir = _lib.Settings.GameDir;
        if (!GameLocator.IsGameDir(dir))
            SetStatus("Игра не найдена — нажми на красную плашку и укажи папку", StatusKind.Error);
        else if (!BepInExInstaller.IsInstalled(dir!))
            SetStatus("Сначала установи BepInEx — нажми на жёлтую плашку", StatusKind.Info);
    }

    // ---------- Служебное ----------

    private void Attach(ModEntry mod)
    {
        mod.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ModEntry.Enabled)) UpdateCounts(); };
        _mods.Add(mod);
    }

    private Brush Res(string key) => (Brush)FindResource(key);

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
