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

    /// <summary>
    /// Où se trouve une fenêtre, telle que Windows la retient. Rend
    /// <c>null</c> si la fenêtre n'existe plus.
    /// </summary>
    WindowPlacement? GetPlacement(nint handle);

    /// <summary>
    /// Remet une fenêtre où elle était.
    ///
    /// Windows se charge de la ramener sur un écran présent : un rectangle
    /// enregistré sur un écran depuis débranché n'envoie pas la fenêtre dans
    /// le vide, contrairement à une position posée à la main.
    /// </summary>
    bool SetPlacement(nint handle, WindowPlacement placement);

    /// <summary>
    /// Processus propriétaire d'une fenêtre, ou zéro si elle a disparu.
    ///
    /// Sert à reconnaître nos fenêtres sans dépendre du handle que nous avons
    /// retenu : celui d'une session fraîchement rouverte n'est pas encore
    /// résolu, et les raccourcis se croyaient alors hors de chez eux.
    /// </summary>
    int GetWindowProcessId(nint handle);

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

    /// <summary>
    /// Encombrement du cadre d'une fenêtre ordinaire sur l'écran donné :
    /// bordures et barre de titre.
    ///
    /// Il faut le connaître avant qu'aucune fenêtre n'existe, pour demander à
    /// scrcpy un afficheur de la taille exacte de la zone client. Le jeu fige
    /// la hauteur de sa mise en page à son initialisation : la corriger après
    /// coup ne rattrape rien.
    /// </summary>
    (int Width, int Height) GetWindowChrome(string? monitorDeviceName);

    /// <summary>Met une fenêtre au premier plan et lui donne le focus clavier.</summary>
    void Focus(nint handle);

    /// <summary>
    /// Demande poliment la fermeture d'une fenêtre, comme le ferait un clic
    /// sur sa croix.
    ///
    /// C'est ce qui permet à scrcpy de prévenir son serveur avant de partir.
    /// Tuer le client suffisait tant que la liaison était en USB ; sur une
    /// liaison Wi-Fi, le serveur ne voit pas tout de suite la socket rompue,
    /// survit sur le téléphone et garde son afficheur virtuel ouvert.
    /// </summary>
    void RequestClose(nint handle);

    /// <summary>
    /// Remonte une fenêtre au sommet de la pile sans lui donner le focus.
    ///
    /// C'est ce qui permet de faire suivre l'ordre de la liste à l'ordre des
    /// fenêtres, donc à celui d'Alt+Tab, sans arracher le clavier à la fenêtre
    /// où l'utilisateur est en train de jouer.
    /// </summary>
    void Raise(nint handle);

    /// <summary>
    /// Change le titre d'une fenêtre. Le rappel du raccourci y figure, et doit
    /// suivre une modification faite dans l'éditeur : scrcpy ne fixe son titre
    /// qu'au démarrage.
    /// </summary>
    void SetTitle(nint handle, string title);

    /// <summary>Retire ou rétablit la bordure, pour le mode plein écran sans bordure.</summary>
    void SetBorderless(nint handle, bool borderless);

    /// <summary>Handle de la fenêtre active, tous processus confondus.</summary>
    nint GetForegroundWindow();
}
