using System.Windows;

using Microsoft.Win32;

namespace DtHub.App.Services;

/// <summary>
/// Applique le thème clair ou sombre en suivant Windows. Il n'y a pas de
/// réglage : suivre le système donne toujours le bon résultat et fait un
/// réglage de moins.
/// </summary>
public sealed class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string LightThemeValue = "AppsUseLightTheme";

    private static readonly Uri LightPalette = new("Themes/Palette.xaml", UriKind.Relative);
    private static readonly Uri DarkPalette = new("Themes/PaletteDark.xaml", UriKind.Relative);

    /// <summary>Applique le thème du système à l'application entière.</summary>
    public void ApplySystemTheme()
    {
        var dictionaries = Application.Current?.Resources.MergedDictionaries;
        if (dictionaries is null || dictionaries.Count == 0)
        {
            return;
        }

        // La palette est toujours le premier dictionnaire fusionné : on la
        // remplace en place pour que les références dynamiques suivent.
        dictionaries[0] = new ResourceDictionary
        {
            Source = IsSystemDark() ? DarkPalette : LightPalette,
        };
    }

    /// <summary>
    /// Lit la préférence système. Une clé absente ou illisible est traitée
    /// comme un thème clair, ce qui est le comportement par défaut de Windows.
    /// </summary>
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);

            return key?.GetValue(LightThemeValue) is int value && value == 0;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException
                                          or UnauthorizedAccessException
                                          or System.IO.IOException)
        {
            return false;
        }
    }
}
