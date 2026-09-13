namespace DtHub.Core.Windows;

/// <summary>Creates a Windows shortcut.</summary>
public interface IShortcutWriter
{
    /// <summary>
    /// Writes a shortcut, or rewrites it if it exists. Returns false
    /// if Windows refused: a missing shortcut is not worth blocking
    /// anything over.
    /// </summary>
    /// <param name="linkPath">The shortcut file to write.</param>
    /// <param name="targetPath">What it opens.</param>
    /// <param name="description">What Windows shows on hover.</param>
    bool Write(string linkPath, string targetPath, string? description = null);
}
