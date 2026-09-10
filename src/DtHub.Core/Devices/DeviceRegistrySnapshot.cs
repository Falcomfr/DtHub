namespace DtHub.Core.Devices;

/// <summary>
/// Ce que le registre sait, rendu en une seule lecture : les appareils
/// mémorisés et ceux dont l'association a été rompue.
///
/// Les deux vivent dans le même fichier, et le registre le relit à chaque
/// demande. Les réclamer séparément le faisait donc lire deux fois par
/// balayage, pour rien.
/// </summary>
/// <param name="Known">Appareils mémorisés, connectés ou non.</param>
/// <param name="Discarded">Identifiants des appareils écartés.</param>
public readonly record struct DeviceRegistrySnapshot(
    IReadOnlyList<AndroidDevice> Known,
    IReadOnlySet<string> Discarded);
