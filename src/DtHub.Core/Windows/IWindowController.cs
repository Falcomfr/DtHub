namespace DtHub.Core.Windows;

/// <summary>Une fenêtre de premier niveau appartenant à un processus.</summary>
public readonly record struct WindowHandleInfo(nint Handle, string Title, int ProcessId);

/// <summary>
/// Accès aux fenêtres du bureau. Isolé derrière une interface pour que la
/// logique de disposition reste testable sans manipuler de vraies fenêtres.
/// </summary>
public interface IWindowController
{
    /// <summary>Écrans connectés, avec leur zone utilisable.</summary>
    IReadOnlyList<MonitorInfo> GetMonitors();

    /// <summary>Fenêtres visibles de premier niveau appartenant à un processus.</summary>
    IReadOnlyList<WindowHandleInfo> FindWindows(int processId);

    /// <summary>Vrai si le handle désigne encore une fenêtre existante.</summary>
    bool IsWindow(nint handle);

    /// <summary>Position et taille actuelles, ou <c>null</c> si la fenêtre a disparu.</summary>
    ScreenRect? GetWindowRect(nint handle);

    /// <summary>
    /// Taille de la zone client, hors barre de titre et bordures. C'est elle
    /// que scrcpy remplit : calculer le rapport sur le rectangle extérieur
    /// laisserait des bandes noires.
    /// </summary>
    ScreenRect? GetClientRect(nint handle);

    /// <summary>Déplace et redimensionne une fenêtre.</summary>
    void MoveWindow(nint handle, ScreenRect rect, bool bringToFront = false);

    /// <summary>Met une fenêtre au premier plan et lui donne le focus clavier.</summary>
    void Focus(nint handle);

    /// <summary>Retire ou rétablit la bordure, pour le mode plein écran sans bordure.</summary>
    void SetBorderless(nint handle, bool borderless);

    /// <summary>Handle de la fenêtre active, tous processus confondus.</summary>
    nint GetForegroundWindow();
}
