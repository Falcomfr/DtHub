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

    /// <summary>Ce que le message annonce, « loaded » ou « step ».</summary>
    public string Kind { get; init; } = string.Empty;

    /// <summary>Bloc d'introduction du guide, tel quel.</summary>
    public string? Intro { get; init; }

    /// <summary>Bloc de chaîne du guide, tel quel.</summary>
    public string? Chain { get; init; }

    /// <summary>Le texte de chaque étape.</summary>
    public IReadOnlyList<string> Steps { get; init; } = [];

    /// <summary>Vrai quand la première étape est bien le départ de la quête.</summary>
    public bool Departure { get; init; }

    /// <summary>Rang de l'étape atteinte.</summary>
    public int Index { get; init; }

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

    private static IReadOnlyList<string> Lines(JsonElement root)
    {
        if (!root.TryGetProperty("steps", out var steps)
            || steps.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. steps
                .EnumerateArray()
                .Select(s => s.ValueKind == JsonValueKind.String ? s.GetString() ?? string.Empty : string.Empty),
        ];
    }
}
