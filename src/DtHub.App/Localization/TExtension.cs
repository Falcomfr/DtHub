using System.Windows.Markup;
using DtHub.Core.Localization;

namespace DtHub.App.Localization;

/// <summary>
/// Renders a translated text from XAML: <c>Content="{loc:T Quit}"</c>.
///
/// WPF strips the "Extension" suffix from the name: the class is
/// called <c>TExtension</c> and is written <c>T</c> in markup. The
/// name is short because it appears on every label of every window.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TExtension : MarkupExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
