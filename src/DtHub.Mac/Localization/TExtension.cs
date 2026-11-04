using DtHub.Core.Localization;

namespace DtHub.Mac.Localization;

/// <summary>
/// The PC's <c>{loc:T Quit}</c> (src/DtHub.App/Localization/TExtension.cs)
/// for Avalonia: a translated text, read once when the view is built, as
/// on the PC. The language is therefore chosen before the first window.
///
/// Not derived from MarkupExtension: Avalonia's compiler takes any class
/// with a ProvideValue, and a string return type lets it feed a Text
/// property without a cast at run time.
/// </summary>
public sealed class TExtension
{
    public TExtension()
    {
    }

    public TExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public string ProvideValue(IServiceProvider serviceProvider) => Strings.Get(Key);
}
