using System.Runtime.InteropServices;

using DtHub.Core.Windows;

namespace DtHub.Infrastructure.Windows;

/// <summary>
/// Écrit un raccourci par l'interface du shell de Windows.
///
/// C'est la seule voie depuis l'application : le script PowerShell du dépôt
/// fait la même chose, mais PowerShell est proscrit à l'exécution.
///
/// Le raccourci vise l'exécutable là où il se trouve, sans rien copier ni
/// déplacer. L'application le récrit à chaque démarrage : déplacer le fichier
/// suffit alors à corriger le raccourci, sans rien demander à personne.
/// </summary>
public sealed class Win32ShortcutWriter : IShortcutWriter
{
    /// <inheritdoc />
    public bool Write(string linkPath, string targetPath, string? description = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(linkPath);
        ArgumentException.ThrowIfNullOrEmpty(targetPath);

        try
        {
            var directory = Path.GetDirectoryName(linkPath);

            if (directory is not null)
            {
                _ = Directory.CreateDirectory(directory);
            }

            var link = (IShellLinkW)new ShellLink();

            link.SetPath(targetPath);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);

            if (!string.IsNullOrEmpty(description))
            {
                link.SetDescription(description);
            }

            ((IPersistFile)link).Save(linkPath, fRemember: true);

            return true;
        }
        catch (Exception exception) when (exception is COMException
            or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    [ComImport]
    // Non scellée : le compilateur refuse de convertir une classe scellée vers
    // une interface qu'elle ne déclare pas, alors que c'est précisément ainsi
    // qu'on obtient un objet du shell.
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(
            [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file,
            int maxPath,
            nint find,
            int flags);

        void GetIDList(out nint list);

        void SetIDList(nint list);

        void GetDescription(
            [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder name, int maxName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);

        void GetWorkingDirectory(
            [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder directory, int maxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);

        void GetArguments(
            [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder arguments, int maxArguments);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int show);

        void SetShowCmd(int show);

        void GetIconLocation(
            [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder icon,
            int maxIcon,
            out int index);

        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string icon, int index);

        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);

        void Resolve(nint window, int flags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);

        [PreserveSig]
        int IsDirty();

        void Load([MarshalAs(UnmanagedType.LPWStr)] string file, uint mode);

        void Save(
            [MarshalAs(UnmanagedType.LPWStr)] string? file,
            [MarshalAs(UnmanagedType.Bool)] bool fRemember);

        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string file);

        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string file);
    }
}
