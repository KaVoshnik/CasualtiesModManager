using System.Windows;
using System.Windows.Input;

namespace CasualtiesModManager;

public partial class LinkDialog : Window
{
    private readonly Func<string, string?>? _validator;

    /// <summary>Введённая ссылка (после нажатия «Готово»).</summary>
    public string Url { get; private set; } = "";

    /// <param name="validator">Возвращает текст ошибки или null, если ссылка подходит.</param>
    public LinkDialog(string title, string hint, string initial, Func<string, string?>? validator = null)
    {
        InitializeComponent();
        _validator = validator;
        TitleText.Text = title;
        HintText.Text = hint;
        UrlBox.Text = initial;
        Loaded += (_, _) =>
        {
            UrlBox.Focus();
            UrlBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var text = UrlBox.Text.Trim();
        var error = _validator?.Invoke(text);
        if (error != null)
        {
            ErrorText.Text = error;
            ErrorText.Visibility = Visibility.Visible;
            return;
        }
        Url = text;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Title_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }
}
