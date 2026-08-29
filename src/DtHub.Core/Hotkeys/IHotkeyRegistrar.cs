namespace DtHub.Core.Hotkeys;

/// <summary>
/// Enregistre les raccourcis auprès du système. L'enregistrement est
/// volontairement conditionnel : hors des fenêtres de DT Hub, les
/// combinaisons doivent revenir aux autres logiciels, sans quoi Ctrl+Tab
/// cesserait de fonctionner dans un navigateur.
/// </summary>
public interface IHotkeyRegistrar : IDisposable
{
    /// <summary>Déclenché quand une combinaison enregistrée est pressée.</summary>
    event EventHandler<HotkeyAction>? HotkeyPressed;

    /// <summary>
    /// Déclenché quand la fenêtre active du bureau change. L'appelant décide
    /// alors s'il faut activer ou désactiver les raccourcis.
    /// </summary>
    event EventHandler<nint>? ForegroundWindowChanged;

    /// <summary>Vrai si les raccourcis sont actuellement enregistrés.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Définit les raccourcis à enregistrer. Prend effet immédiatement si les
    /// raccourcis sont actifs.
    /// </summary>
    /// <returns>
    /// Actions dont le raccourci a été refusé par le système, généralement
    /// parce qu'un autre logiciel le détient déjà.
    /// </returns>
    Task<IReadOnlyList<HotkeyAction>> ApplyAsync(HotkeySet hotkeys);

    /// <summary>Active ou désactive l'interception.</summary>
    Task SetEnabledAsync(bool enabled);
}
