using System.Runtime.InteropServices;

using DtHub.Core.Windows;

namespace DtHub.Infrastructure.Windows;

/// <summary>
/// Writes a shortcut through the Windows shell interface.
///
/// This is the only path from the application: the repository's
/// PowerShell script does the same thing, but PowerShell is
/// forbidden at runtime.
///
/// The shortcut targets the executable wherever it is, without
/// copying or moving anything. The application rewrites it on
/// every startup: moving the file is then enough to fix the
/// shortcut, without asking anyone anything.
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
            // Deliberate silence: returning false reports the
            // failure to the caller, who logs it. A missing Start
            // menu shortcut prevents nothing.
            return false;
        }
    }

    [ComImport]
    // Not sealed: the compiler refuses to convert a sealed class to
    // an interface it does not declare, even though that is exactly
    // how a shell object is obtained.
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
