namespace DtHub.Core.Apps;

/// <summary>Un composant Android lançable, sous la forme <c>paquet/activité</c>.</summary>
public sealed record AppComponent(string PackageName, string ClassName)
{
    /// <summary>Notation attendue par <c>am start -n</c>.</summary>
    public string Value => $"{PackageName}/{ClassName}";

    public override string ToString() => Value;
}
