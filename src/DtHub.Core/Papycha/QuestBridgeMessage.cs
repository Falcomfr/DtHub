using System.Text.Json;

namespace DtHub.Core.Papycha;

/// <summary>
/// Ce que le script posé dans la page dit à la fenêtre, une fois lu.
///
/// La lecture est ici, hors de la fenêtre, parce qu'elle doit tenir devant
/// n'importe quoi. Le script est posé sur tout document que la fenêtre charge,
/// et n'importe quelle page peut poster son propre message : le pont n'est pas
/// une conversation avec nous-mêmes.
///
/// La fenêtre lisait « kind » sans précaution. Un message qui n'a pas ce champ
/// levait donc une exception dans un gestionnaire d'événement, où personne ne
/// la rattrape, ce qui emportait l'application ; un message qui n'est pas du
/// texte la levait avant même d'être lu. Trois lignes de JavaScript sur une
/// page du site suffisaient.
/// </summary>
public sealed record QuestBridgeMessage
{
    /// <summary>Le guide est cadré et lu : voici ce qu'il contient.</summary>
    public const string Loaded = "loaded";

    /// <summary>On a fait défiler la page jusqu'à une autre étape.</summary>
    public const string Step = "step";

    /// <summary>
    /// On a atteint le bas du guide, ou on l'a quitté.
    ///
    /// Distinct de l'étape, et c'est la correction d'un défaut : lier
    /// l'affichage de fin à la dernière étape le faisait clignoter, le bloc
    /// rognant la hauteur de la vue et déplaçant du même coup l'étape détectée.
    /// </summary>
    public const string End = "end";

    /// <summary>Ce que le message annonce, « loaded », « step » ou « end ».</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Bloc d'introduction du guide, tel quel.</summary>
    public string? Intro { get; init; }

    /// <summary>Bloc de chaîne du guide, tel quel.</summary>
    public string? Chain { get; init; }

    /// <summary>Chaque étape, avec sa nature.</summary>
    public IReadOnlyList<QuestStep> Steps { get; init; } = [];

    /// <summary>Vrai quand la première étape est bien le départ de la quête.</summary>
    public bool Departure { get; init; }

    /// <summary>Rang de l'étape atteinte.</summary>
    public int Index { get; init; }

    /// <summary>Vrai quand la page est arrivée en bas, pour un message « end ».</summary>
    public bool AtEnd { get; init; }

    /// <summary>
    /// Lit un message, ou rend <c>null</c> quand il n'y a rien à en tirer.
    ///
    /// Rien n'est exigé sinon le genre, et le rang pour un message d'étape :
    /// tout le reste manque par endroits sur le site lui-même, une page de
    /// donjon n'ayant ni bloc de départ ni chaîne.
    /// </summary>
    public static QuestBridgeMessage? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // Silence assumé : un message que le pont n'a pas écrit, ou qu'il a
            // écrit de travers, ne dit rien de la page. On l'ignore plutôt que
            // de faire tomber la fenêtre sur du texte venu du site.
            return null;
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var name = kind.GetString() ?? string.Empty;

            if (string.Equals(name, End, StringComparison.Ordinal))
            {
                return new QuestBridgeMessage { Kind = End, AtEnd = Flag(root, "at") };
            }

            if (string.Equals(name, Step, StringComparison.Ordinal))
            {
                return root.TryGetProperty("index", out var index)
                    && index.ValueKind == JsonValueKind.Number
                    && index.TryGetInt32(out var rank)
                    ? new QuestBridgeMessage { Kind = Step, Index = rank }
                    : null;
            }

            return new QuestBridgeMessage
            {
                Kind = name,
                Intro = Text(root, "intro"),
                Chain = Text(root, "chain"),
                Steps = Lines(root),
                Departure = Flag(root, "departure"),
            };
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<QuestStep> Lines(JsonElement root)
    {
        if (!root.TryGetProperty("steps", out var steps)
            || steps.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return [.. steps.EnumerateArray().Select(StepOf)];
    }

    /// <summary>
    /// Lit une étape, sous l'une ou l'autre des deux formes.
    ///
    /// Le pont l'écrit en objet, texte et nature. Une chaîne nue est acceptée
    /// pour ce qu'elle est, une étape sans nature déclarée, donc une consigne :
    /// c'est la forme qu'écrivait le pont avant qu'il ne porte la nature, et
    /// c'est aussi tout ce qu'une page du site saurait poster d'elle-même. Le
    /// rang, lui, est gardé quoi qu'il arrive : une étape illisible devient une
    /// étape vide et non une étape en moins, sans quoi les rangs glisseraient.
    /// </summary>
    private static QuestStep StepOf(JsonElement step) => step.ValueKind switch
    {
        JsonValueKind.String => new QuestStep(step.GetString() ?? string.Empty, IsTitle: false),
        JsonValueKind.Object => new QuestStep(
            Text(step, "text") ?? string.Empty,
            Flag(step, "title")),
        _ => new QuestStep(string.Empty, IsTitle: false),
    };
}

/// <summary>
/// Une étape d'un guide, telle que le pont la rapporte.
///
/// La nature commande l'affichage. Un titre de section expose et se montre tel
/// quel ; une consigne ordonne, et la fenêtre n'en montre rien, le rang suffisant
/// à s'y rendre. La distinction vient du site : une fiche de donjon, de raid, de
/// tanière ou de chemin se lit par ses titres, un guide de quête par ses
/// paragraphes.
/// </summary>
/// <param name="Text">Le texte de l'étape, vide pour le départ d'un lieu.</param>
/// <param name="IsTitle">Vrai quand l'étape est un titre de section du site.</param>
public readonly record struct QuestStep(string Text, bool IsTitle);
