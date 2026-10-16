using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CasualtiesModManager.Services;

namespace CasualtiesModManager;

/// <summary>Настройки менеджера: язык, тема, обновления, багрепорт.</summary>
public partial class SettingsDialog : Window
{
    private readonly MainWindow _main;
    private readonly ModLibrary _lib;

    public SettingsDialog(MainWindow main, ModLibrary lib)
    {
        InitializeComponent();
        _main = main;
        _lib = lib;

        AutoCheck.IsChecked = lib.Settings.AutoCheckSelfUpdate;
        RefreshTexts();
        Highlight();
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged()
    {
        RefreshTexts();
        Highlight();
    }

    private void Highlight()
    {
        void Mark(IEnumerable<Button> buttons, string current)
        {
            foreach (var b in buttons)
            {
                var active = (b.Tag as string) == current;
                b.SetResourceReference(Control.ForegroundProperty, active ? "AccentBrush" : "TextBrush");
                b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }
        Mark(new[] { LangEn, LangRu }, Loc.Lang);
        Mark(new[] { ThemeDark, ThemeLight, ThemeSystem }, Theme.Normalize(_lib.Settings.Theme));
    }

    /// <summary>Подписи, которые собираются в коде (версия, найденное обновление).</summary>
    private void RefreshTexts()
    {
        VersionText.Text = Loc.T("Set_Version", AppInfo.VersionText);
        var u = _main.PendingSelfUpdate;
        if (u != null)
        {
            UpdateText.Text = Loc.T("Upd_Available", u.Version.ToString(3));
            InstallButton.Content = Loc.T("Upd_Button", u.Version.ToString(3));
            InstallButton.Visibility = Visibility.Visible;
        }
        else
        {
            UpdateText.Text = "";
            InstallButton.Visibility = Visibility.Collapsed;
        }
    }

    private void Lang_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string lang }) Loc.SetLanguage(lang);   // окно подпишется на LanguageChanged
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string mode }) return;
        _main.SetTheme(mode);
        Highlight();
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        UpdateText.Text = Loc.T("Upd_Checking");
        var (update, error) = await _main.CheckSelfUpdateAsync(announce: true);
        CheckButton.IsEnabled = true;

        RefreshTexts();
        if (error != null) UpdateText.Text = Loc.T("Upd_Failed", error);
        else if (update == null) UpdateText.Text = Loc.T("Upd_UpToDate");
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        Close();   // дальше прогресс виден в строке состояния главного окна
        await _main.InstallSelfUpdateAsync();
    }

    private void AutoCheck_Click(object sender, RoutedEventArgs e)
    {
        _lib.Settings.AutoCheckSelfUpdate = AutoCheck.IsChecked == true;
        _lib.Save();
    }

    private void CopyReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(DiagnosticsReport.Build(_lib));
            CopyText.SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");
            CopyText.Text = Loc.T("Set_Copied");
        }
        catch (Exception ex)
        {
            CopyText.SetResourceReference(TextBlock.ForegroundProperty, "DangerBrush");
            CopyText.Text = Loc.T("Set_CopyFailed", ex.Message);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Title_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
