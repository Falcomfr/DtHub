using System.Text.Json.Serialization;

using DtHub.Core.Hotkeys;
using DtHub.Core.Windows;

namespace DtHub.Core.Settings;

/// <summary>
/// Forme persistée de <c>settings.json</c>. Les valeurs par défaut sont
/// viables : un fichier absent, partiel ou modifié à la main doit donner une
/// configuration utilisable.
/// </summary>
public sealed class AppSettingsDocument
{
    public const int CurrentSchemaVersion = 9;

    /// <summary>Tailles livrées d'origine, en pourcentage de la zone utilisable.</summary>
    public static readonly int[] DefaultSizePercentages = [40, 60, 80, 100];

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Instances mémorisées, y compris celles qui ne sont pas cochées.</summary>
    public List<StoredInstance> Instances { get; set; } = [];

    // Fenêtres

    /// <summary>Position du bloc de fenêtres de jeu dans l'écran.</summary>
    public WindowAnchor GameAnchor { get; set; } = WindowAnchor.MiddleLeft;

    /// <summary>
    /// Tailles proposées, en pourcentage de la zone utilisable de l'écran.
    /// Elles sont donc proportionnelles à l'écran employé.
    /// </summary>
    public List<int> SizePercentages { get; set; } = [.. DefaultSizePercentages];

    /// <summary>Taille retenue, par son indice. La dernière est le plein écran.</summary>
    public int SizeIndex { get; set; } = 1;

    /// <summary>
    /// Taille posée au curseur, en pourcentage. Zéro quand c'est un raccourci
    /// qui a décidé, et que l'indice fait donc foi.
    /// </summary>
    public int CustomSizePercent { get; set; }



    /// <summary>
    /// Vrai si le configurateur était affiché à la sortie. Il retrouve cet
    /// état au lancement suivant.
    /// </summary>
    public bool ConfiguratorVisible { get; set; } = true;

    /// <summary>
    /// Vrai si le suivi de quêtes était affiché à la sortie. Il rouvre alors
    /// au lancement suivant, sur la dernière quête lue.
    /// </summary>
    public bool QuestsVisible { get; set; }

    /// <summary>
    /// La dernière quête ouverte dans le suivi. Vide tant qu'aucune ne l'a été,
    /// et la fenêtre rouvre alors sur sa liste.
    /// </summary>
    public string LastQuestUrl { get; set; } = string.Empty;

    /// <summary>
    /// L'étape où l'on était dans ce guide. Seule l'adresse était retenue : on
    /// rouvrait le bon guide à sa première étape, et il fallait refaire le
    /// chemin. Zéro vaut « la première », qui est aussi le repli quand la page
    /// en compte moins qu'avant.
    /// </summary>
    public int LastQuestStep { get; set; }

    /// <summary>
    /// Vrai quand l'application se met à jour toute seule : elle télécharge la
    /// livraison en fond et l'installe en quittant, jamais en pleine session.
    /// Décoché, elle se contente de dire qu'une version existe.
    /// </summary>
    public bool UpdatesAutomatic { get; set; } = true;

    /// <summary>
    /// Où sont les fenêtres de l'application, par nom.
    ///
    /// Une table plutôt qu'un champ par fenêtre : elles se ressemblent toutes
    /// sur ce point, et une nouvelle n'a alors rien à ajouter ici.
    /// </summary>
    public Dictionary<string, WindowPlacement> WindowPlacements { get; set; } = [];

    // Mirroring

    public bool AudioEnabled { get; set; }
    public bool ClipboardSyncEnabled { get; set; } = true;
    // Le jeu s'affiche en paysage : un écran virtuel vertical le centrerait
    // en 16:9 au milieu d'une fenêtre haute, avec deux larges bandes noires.
    public int VirtualDisplayWidth { get; set; } = 1920;
    public int VirtualDisplayHeight { get; set; } = 1080;
    public int VirtualDisplayDpi { get; set; } = 240;

    /// <summary>
    /// Compromis entre finesse de l'image et charge de la machine. La valeur
    /// moyenne est celle d'origine : rien ne change tant qu'on n'y touche pas.
    /// </summary>
    public StreamQuality Quality { get; set; } = StreamQuality.Medium;

    /// <summary>
    /// Distance apparente dans le jeu. Comme la qualité, elle est figée à
    /// l'ouverture d'une session : la changer rouvre les fenêtres.
    /// </summary>
    public GameZoom GameZoom { get; set; } = GameZoom.Normal;

    /// <summary>Paquet du jeu. Réglable pour survivre à un changement amont.</summary>
    public string PackageName { get; set; } = Dofus.DofusPackages.DofusTouch;

    // Raccourcis
    public List<StoredHotkey> Hotkeys { get; set; } = [];
}

/// <summary>Une instance mémorisée entre deux lancements.</summary>
public sealed class StoredInstance
{
    public string DeviceId { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string PackageName { get; set; } = string.Empty;

    /// <summary>Nom du profil Android au moment de la découverte, pour l'affichage hors ligne.</summary>
    public string UserName { get; set; } = string.Empty;

    public string DeviceName { get; set; } = string.Empty;

    /// <summary>Nom choisi par l'utilisateur.</summary>
    public string? CustomName { get; set; }

    public string? LaunchComponent { get; set; }

    /// <summary>Vrai si l'instance fait partie du lancement automatique.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Vrai si la fenêtre suit les placements automatiques : parcours au
    /// clavier, replacement, côte à côte, changements de taille.
    ///
    /// Décochée, la fenêtre est laissée où elle est et le reste s'arrange sans
    /// elle. Elle s'ouvre et se ferme comme les autres.
    /// </summary>
    public bool IsManaged { get; set; } = true;

    /// <summary>
    /// Rang de l'instance dans la liste unique, dense de 0 à n-1.
    ///
    /// C'est la seule donnée d'ordre : les instances se trient librement entre
    /// elles, quel que soit leur appareil. Trier là-dessus suffit donc à
    /// obtenir l'ordre d'affichage, d'ouverture et de parcours au clavier.
    /// C'est <see cref="InstanceOrdering.Normalize"/> qui maintient la densité.
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Où la fenêtre a été laissée. <c>null</c> tant qu'elle n'a jamais été
    /// ouverte : le placement retombe alors sur l'ancrage et la taille.
    /// </summary>
    public StoredWindowRect? Window { get; set; }

    /// <summary>
    /// Clé stable de l'instance. Exclue du fichier : elle se déduit des trois
    /// champs qui la composent, et l'écrire n'ajouterait qu'une redondance
    /// qu'une modification à la main pourrait contredire.
    /// </summary>
    [JsonIgnore]
    public string Key => $"{DeviceId}|{UserId}|{PackageName}";
}

/// <summary>
/// Géométrie retenue d'une fenêtre de jeu. L'écran est mémorisé avec elle :
/// un rectangle valable hier peut se retrouver hors de tout écran aujourd'hui,
/// et il ne faut pas y rouvrir une fenêtre invisible.
/// </summary>
public sealed class StoredWindowRect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    /// <summary>
    /// Écran sur lequel la fenêtre se trouvait. Ce nom est positionnel :
    /// débrancher un écran renumérote les suivants. Il ne suffit donc pas, et
    /// les bornes sont mémorisées avec lui.
    /// </summary>
    public string? MonitorDeviceName { get; set; }

    public int MonitorX { get; set; }
    public int MonitorY { get; set; }
    public int MonitorWidth { get; set; }
    public int MonitorHeight { get; set; }

    /// <summary>Rectangle extérieur de la fenêtre.</summary>
    [JsonIgnore]
    public Windows.ScreenRect Bounds => new(X, Y, Width, Height);

    /// <summary>Bornes de l'écran au moment de la capture.</summary>
    [JsonIgnore]
    public Windows.ScreenRect Monitor => new(MonitorX, MonitorY, MonitorWidth, MonitorHeight);

    public static StoredWindowRect From(Windows.ScreenRect rect, Windows.MonitorInfo monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);

        return new StoredWindowRect
        {
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
            MonitorDeviceName = monitor.DeviceName,
            MonitorX = monitor.Bounds.X,
            MonitorY = monitor.Bounds.Y,
            MonitorWidth = monitor.Bounds.Width,
            MonitorHeight = monitor.Bounds.Height,
        };
    }
}

/// <summary>Forme persistée d'un raccourci.</summary>
public sealed class StoredHotkey
{
    public string Action { get; set; } = string.Empty;
    public int VirtualKey { get; set; }
    public HotkeyModifiers Modifiers { get; set; }

    public static StoredHotkey From(HotkeyBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);

        return new StoredHotkey
        {
            Action = binding.Action.ToString(),
            VirtualKey = binding.VirtualKey,
            Modifiers = binding.Modifiers,
        };
    }

    /// <summary>
    /// Reconstruit un raccourci, ou rend <c>null</c> si l'action n'existe
    /// plus. Un fichier écrit par une autre version ne doit pas faire échouer
    /// la lecture.
    /// </summary>
    public HotkeyBinding? ToBinding()
    {
        // « CloseAll » est l'ancien nom de « Quit ». Sans cette équivalence, la
        // combinaison choisie par l'utilisateur serait perdue au renommage.
        var name = string.Equals(Action, "CloseAll", StringComparison.OrdinalIgnoreCase)
            ? nameof(HotkeyAction.Quit)
            : Action;

        return Enum.TryParse<HotkeyAction>(name, ignoreCase: true, out var action)
            ? new HotkeyBinding { Action = action, VirtualKey = VirtualKey, Modifiers = Modifiers }
            : null;
    }
}
