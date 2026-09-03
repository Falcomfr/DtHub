namespace DtHub.App.ViewModels;

/// <summary>
/// Un choix de liste déroulante : ce qui s'affiche, et ce qui est retenu.
///
/// Les deux diffèrent presque toujours dans le panneau des réglages fins :
/// on montre « 12 Mb/s » et l'on garde 12000, on montre « H.265 » et l'on
/// garde « h265 ». Sans ce couple, il faudrait un convertisseur par liste.
/// </summary>
public sealed record IntChoice(string Label, int Value);

/// <summary>Le même, pour un choix qui se retient sous forme de texte.</summary>
public sealed record TextChoice(string Label, string Value);

/// <summary>Le même, pour un choix qui se retient sous forme de nombre décimal.</summary>
public sealed record DoubleChoice(string Label, double Value);
