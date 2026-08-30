namespace DtHub.Core.Storage;

/// <summary>
/// Valeur à retenir quand un fichier de configuration porte un nom
/// d'énumération que cette version ne connaît plus.
///
/// Sans elle, un palier retiré du code rendait tout le fichier illisible : la
/// lecture échouait sur ce seul mot, et l'utilisateur perdait ses instances,
/// ses raccourcis et la géométrie de ses fenêtres. Le repli est déclaré sur
/// l'énumération elle-même, à côté de ses membres, pour qu'on y pense en les
/// modifiant.
/// </summary>
[AttributeUsage(AttributeTargets.Enum)]
public sealed class JsonFallbackAttribute(object value) : Attribute
{
    /// <summary>Membre retenu à la place d'un nom inconnu.</summary>
    public object Value { get; } = value;
}
