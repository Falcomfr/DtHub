namespace DtHub.Core.Windows;

/// <summary>Un écran Windows et sa zone utilisable, barre des tâches exclue.</summary>
public sealed record MonitorInfo
{
    public required string DeviceName { get; init; }

    /// <summary>Rectangle complet de l'écran.</summary>
    public required ScreenRect Bounds { get; init; }

    /// <summary>
    /// Zone utilisable, barre des tâches et barres d'outils exclues. C'est
    /// elle qui sert au dimensionnement en pourcentage : une fenêtre à 90 %
    /// ne doit pas passer sous la barre des tâches.
    /// </summary>
    public required ScreenRect WorkArea { get; init; }

    public bool IsPrimary { get; init; }

    /// <summary>Nom affiché dans les paramètres.</summary>
    public string DisplayName =>
        $"{(IsPrimary ? "Écran principal" : DeviceName)} ({Bounds.Width}x{Bounds.Height})";
}
