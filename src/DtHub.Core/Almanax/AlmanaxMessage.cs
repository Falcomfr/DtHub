using System.Text.Json;

namespace DtHub.Core.Almanax;

/// <summary>
/// Ce que la page nous dit d'une journée, et ce qu'on en retient.
///
/// La lecture est tolérante partout **sauf sur une chose** : le bloc doit être
/// celui de DOFUS Touch. Le portail sert les deux jeux sur la même page, et
/// l'Almanax de DOFUS demande d'autres objets. Un champ manquant se remplace
/// par du vide ; un mauvais jeu ne se remplace par rien, et on refuse.
/// </summary>
public static class AlmanaxMessage
{
    /// <summary>
    /// La journée lue, ou <c>null</c> si le message n'est pas exploitable.
    /// </summary>
    /// <param name="json">Ce que le pont a posté.</param>
    /// <param name="date">Le jour demandé : la page ne le porte pas en clair.</param>
    public static AlmanaxDay? From(string? json, DateOnly date)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        JsonElement root;

        try
        {
            using var document = JsonDocument.Parse(json);

            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // Le pont n'en poste jamais d'autre, mais il est posé sur tout
            // document que la fenêtre charge, et toute page peut appeler
            // « postMessage » avec ce qu'elle veut. Rien à signaler : la
            // fenêtre dira qu'elle n'a pas pu lire, ce qui est le cas.
            return null;
        }

        if (root.ValueKind != JsonValueKind.Object || !AlmanaxReading.IsTouch(Text(root, "heading")))
        {
            return null;
        }

        var offering = AlmanaxReading.Clean(Text(root, "offering"));
        var read = AlmanaxReading.Offering(offering);

        var day = AlmanaxReading.Clean(Text(root, "day"));
        var month = AlmanaxReading.Clean(Text(root, "month"));

        return new AlmanaxDay(
            date,
            string.Join(' ', new[] { day, month }.Where(part => part.Length > 0)),
            offering,
            read?.Quantity,
            read?.Item,
            AlmanaxReading.AfterColon(Text(root, "bonus")),
            AlmanaxReading.Clean(Text(root, "bonusDetail")),
            AlmanaxReading.AfterColon(Text(root, "quest")),
            AlmanaxReading.Clean(Text(root, "meryde")),
            AlmanaxReading.Clean(Text(root, "monthEvent")));
    }

    private static string Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
}
