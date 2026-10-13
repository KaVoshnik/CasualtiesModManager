using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CasualtiesModManager.Services;

namespace CasualtiesModManager;

/// <summary>Настройки менеджера. Пока одна: язык интерфейса.</summary>
public partial class SettingsDialog : Window
{
    private readonly Action<string> _onLanguage;

    /// <param name="onLanguage">Вызывается с "en" или "ru", когда пользователь выбрал язык.</param>
    public SettingsDialog(Action<string> onLanguage)
    {
        InitializeComponent();
        _onLanguage = onLanguage;
        Highlight();
    }

    private void Highlight()
    {
        foreach (var b in new[] { LangEn, LangRu })
        {
            var active = (b.Tag as string) == Loc.Lang;
            b.Foreground = (Brush)FindResource(active ? "AccentBrush" : "TextBrush");
            b.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void Lang_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string lang }) return;
        _onLanguage(lang);
        Highlight();
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
