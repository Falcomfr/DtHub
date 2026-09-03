using System.Windows.Markup;
using DtHub.Core.Localization;

namespace DtHub.App.Localization;

/// <summary>
/// Rend un texte traduit depuis le XAML : <c>Content="{loc:T Quit}"</c>.
///
/// WPF retire le suffixe « Extension » du nom : la classe s'appelle
/// <c>TExtension</c> et s'écrit <c>T</c> dans le balisage. Le nom est court
/// parce qu'il paraît sur chaque libellé de chaque fenêtre.
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
