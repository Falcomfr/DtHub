using DtHub.Core.Processes;
using DtHub.Core.Sessions;

namespace DtHub.Core.Scrcpy;

/// <summary>Cycle de vie d'une session de mirroring.</summary>
public enum ScrcpySessionState
{
    /// <summary>scrcpy démarre, l'afficheur n'est pas encore créé.</summary>
    Starting,

    /// <summary>Session ouverte, application lancée.</summary>
    Running,

    /// <summary>La session n'a pas pu s'ouvrir, ou s'est interrompue en erreur.</summary>
    Failed,

    /// <summary>Session fermée normalement.</summary>
    Stopped,
}

/// <summary>
/// Une fenêtre de mirroring ouverte par DT Hub. L'objet vit aussi longtemps
/// que le processus scrcpy correspondant, et porte de quoi le retrouver, le
/// déplacer et le fermer.
/// </summary>
public sealed class ScrcpySession
{
    internal ScrcpySession(string id, LaunchTarget target, string windowTitle, IProcessSession process)
    {
        Id = id;
        Target = target;
        WindowTitle = windowTitle;
        Process = process;
        StartedUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Identité de la session, reprise dans le titre de la fenêtre.</summary>
    public string Id { get; }

    public LaunchTarget Target { get; }

    /// <summary>Numéro de série ADB employé au lancement.</summary>
    public string Serial => Target.Serial;

    /// <summary>
    /// Titre exact de la fenêtre scrcpy. C'est par lui que le gestionnaire de
    /// fenêtres retrouve la fenêtre à déplacer.
    /// </summary>
    public string WindowTitle { get; }

    public int ProcessId => Process.ProcessId;

    /// <summary>Afficheur virtuel créé par scrcpy, quand il y en a un.</summary>
    public int? VirtualDisplayId { get; internal set; }

    /// <summary>
    /// Rapport largeur sur hauteur de la source, repris des réglages de la
    /// session. Le gestionnaire de fenêtres s'en sert pour ne pas déformer
    /// l'image : un téléphone est en portrait, une tablette en paysage.
    /// </summary>
    public double SourceAspectRatio { get; internal set; }

    /// <summary>
    /// Fenêtre scrcpy correspondante, une fois retrouvée. Vaut zéro tant que
    /// la fenêtre n'est pas apparue.
    /// </summary>
    public nint WindowHandle { get; internal set; }

    public ScrcpySessionState State { get; internal set; } = ScrcpySessionState.Starting;

    /// <summary>Message affichable expliquant l'échec, le cas échéant.</summary>
    public string? FailureMessage { get; internal set; }

    /// <summary>
    /// Nature du refus. Sert à décider si une seconde tentative a un sens,
    /// question à laquelle le message affiché ne répond pas.
    /// </summary>
    public ScrcpyFailureKind FailureKind { get; internal set; } = ScrcpyFailureKind.None;

    public DateTimeOffset StartedUtc { get; }

    /// <summary>
    /// Temps mis par le téléphone à ouvrir l'afficheur virtuel, en
    /// millisecondes. C'est la seule partie du démarrage qui doive être
    /// sérialisée : la mesurer dit combien coûte vraiment l'attente.
    /// </summary>
    public long DisplayReadyMs { get; set; }

    /// <summary>Temps total du démarrage, afficheur et ouverture du jeu.</summary>
    public long StartupMs { get; set; }

    /// <summary>Nom affiché dans la liste des sessions.</summary>
    public string DisplayName => Target.DisplayName;

    public bool IsAlive => State is ScrcpySessionState.Starting or ScrcpySessionState.Running;

    /// <summary>
    /// Dernières lignes écrites par scrcpy. Conservées pour que l'appelant
    /// puisse les journaliser en cas d'échec : sans elles, un refus de scrcpy
    /// se résume à « la session n'a pas pu s'ouvrir ».
    /// </summary>
    public IReadOnlyList<string> RecentOutput
    {
        get
        {
            lock (_output)
            {
                return [.. _output];
            }
        }
    }

    /// <summary>Ligne de commande employée, pour le diagnostic.</summary>
    public string CommandLine { get; internal set; } = string.Empty;

    private const int MaxRetainedLines = 60;

    private readonly Queue<string> _output = new();

    internal void Record(string line)
    {
        lock (_output)
        {
            _output.Enqueue(line);

            while (_output.Count > MaxRetainedLines)
            {
                _output.Dequeue();
            }
        }
    }

    internal IProcessSession Process { get; }
}
