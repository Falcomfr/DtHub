namespace DtHub.Core.Windows;

/// <summary>Pose un raccourci Windows.</summary>
public interface IShortcutWriter
{
    /// <summary>
    /// Écrit un raccourci, ou le récrit s'il existe. Rend faux si Windows a
    /// refusé : un raccourci manquant ne vaut pas d'empêcher quoi que ce soit.
    /// </summary>
    /// <param name="linkPath">Le fichier de raccourci à écrire.</param>
    /// <param name="targetPath">Ce qu'il ouvre.</param>
    /// <param name="description">Ce que Windows montre au survol.</param>
    bool Write(string linkPath, string targetPath, string? description = null);
}
