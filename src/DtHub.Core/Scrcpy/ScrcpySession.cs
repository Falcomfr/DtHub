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

    public DateTimeOffset StartedUtc { get; }

    /// <summary>Nom affiché dans la liste des sessions.</summary>
    public string DisplayName => Target.DisplayName;

    public bool IsAlive => State is ScrcpySessionState.Starting or ScrcpySessionState.Running;

    internal IProcessSession Process { get; }
}
