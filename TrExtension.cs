using System.Windows.Data;
using System.Windows.Markup;
using CasualtiesModManager.Services;

namespace CasualtiesModManager;

/// <summary>
/// Подстановка перевода в XAML: Text="{loc:Tr Key}". Привязка к Loc.Instance, поэтому
/// при смене языка тексты обновляются сразу, без перезапуска.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public class TrExtension : MarkupExtension
{
    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
