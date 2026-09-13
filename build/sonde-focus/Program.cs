// Sonde de développement : qui détient vraiment le focus clavier.
//
// D73 note que cette mesure avait dû être abandonnée, l'antivirus refusant le
// script PowerShell qui la portait. Le refus vise le script, analysé par AMSI,
// et non les fonctions : compilée, la même lecture passe. La sonde est donc
// en lecture seule et le restera, pour qu'elle reste au-dessus de tout soupçon.
// Elle n'attache aucune file d'entrée, ne pose aucun focus, n'envoie aucune
// frappe : GetGUIThreadInfo et les accesseurs de titre, rien d'autre.
//
//   dotnet.exe run --project build/sonde-focus
//   dotnet.exe run --project build/sonde-focus -- --suivre 30
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

// Sizes are read in physical pixels, as capture-window.ps1 and list-windows.ps1
// already report them. Without this, Windows virtualises every rectangle to the
// primary monitor's scale: a 1920x1177 tabbed frame on a 150 % display came out
// as 1280x785, which cannot be compared with what the application itself logs,
// and reads as a layout defect that does not exist.
_ = Natives.SetProcessDpiAwarenessContext(Natives.PerMonitorAwareV2);

var secondes = 0;

if (args.Contains("--suivre", StringComparer.Ordinal))
{
    var apres = Array.IndexOf(args, "--suivre") + 1;

    secondes = apres < args.Length
        && int.TryParse(args[apres], CultureInfo.InvariantCulture, out var lu)
            ? lu
            : 20;
}

Processus();

if (secondes == 0)
{
    Etat();
    return;
}

// Mode suivi : n'imprimer que les changements, pour que la trace reste lisible
// pendant qu'on clique d'un onglet à l'autre.
Console.WriteLine($"suivi pendant {secondes} s, seuls les changements sont imprimés");
Console.WriteLine();

var jusqua = DateTime.UtcNow.AddSeconds(secondes);
var precedent = string.Empty;

while (DateTime.UtcNow < jusqua)
{
    var maintenant = Resume();

    if (!string.Equals(maintenant, precedent, StringComparison.Ordinal))
    {
        precedent = maintenant;
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}]");
        Etat();
        Console.WriteLine();
    }

    Thread.Sleep(250);
}

static void Processus()
{
    foreach (var nom in new[] { "DtHub", "scrcpy", "adb" })
    {
        var identifiants = Process.GetProcessesByName(nom).Select(p => p.Id).ToList();

        Console.WriteLine(
            $"{nom,-8} : {(identifiants.Count == 0 ? "(aucun)" : string.Join(", ", identifiants))}");
    }

    Console.WriteLine();
}

static string Resume()
{
    var premierPlan = Natives.GetForegroundWindow();
    var fil = Natives.GetWindowThreadProcessId(premierPlan, out _);
    var info = Info(fil);

    return string.Create(
        CultureInfo.InvariantCulture, $"{premierPlan}/{info.hwndActive}/{info.hwndFocus}");
}

static void Etat()
{
    var premierPlan = Natives.GetForegroundWindow();
    var fil = Natives.GetWindowThreadProcessId(premierPlan, out _);

    Console.WriteLine($"  premier plan : {Decrire(premierPlan)}");

    var info = Info(fil);

    if (info.cbSize == 0)
    {
        Console.WriteLine("  (GetGUIThreadInfo a échoué sur ce fil)");
        return;
    }

    Console.WriteLine($"  active       : {Decrire(info.hwndActive)}");
    Console.WriteLine($"  FOCUS        : {Decrire(info.hwndFocus)}");

    if (info.hwndCapture != 0)
    {
        Console.WriteLine($"  capture      : {Decrire(info.hwndCapture)}");
    }

    // Le focus est un état par fil, pas par bureau : on peut donc le lire sur
    // le fil d'interface de chaque processus concerné sans qu'il soit au
    // premier plan, et sans rien attacher. C'est ce relevé-là qui dit si le
    // clavier peut atteindre le jeu logé.
    Console.WriteLine();
    Console.WriteLine("  files d'entrée, par fil d'interface :");

    Dictionary<uint, string> fils = [];

    _ = Natives.EnumWindows(
        (fenetre, _) =>
        {
            var filFenetre = Natives.GetWindowThreadProcessId(fenetre, out var processus);
            var nom = Nom(processus);

            // explorer et firefox servent de témoins : sûrement pas attachés,
            // ils disent ce que la lecture rend pour un fil ordinaire.
            if (nom is "DtHub" or "scrcpy" or "explorer" or "firefox")
            {
                fils.TryAdd(filFenetre, $"{nom}({processus})");
            }

            return true;
        },
        0);

    foreach (var (filInterface, qui) in fils.OrderBy(f => f.Value, StringComparer.Ordinal))
    {
        var sien = Info(filInterface);

        Console.WriteLine(
            sien.cbSize == 0
                ? $"    fil {filInterface} {qui} : illisible"
                : $"    fil {filInterface} {qui} : focus = {Decrire(sien.hwndFocus)}");
    }

    Console.WriteLine();
    List<string> libres = [];

    _ = Natives.EnumWindows(
        (fenetre, parametre) =>
        {
            _ = parametre;
            _ = Natives.GetWindowThreadProcessId(fenetre, out var processus);

            if (Nom(processus) == "scrcpy")
            {
                libres.Add($"    {Decrire(fenetre)}");
            }

            return true;
        },
        0);

    if (libres.Count > 0)
    {
        Console.WriteLine("  fenêtres de jeu libres :");

        foreach (var libre in libres)
        {
            Console.WriteLine(libre);
        }
    }

    // Les fenêtres logées : filles d'une fenêtre à nous, mais tenues par un
    // autre processus. C'est exactement la population qui ne peut pas recevoir
    // le clavier tant qu'aucune file d'entrée n'est attachée.
    List<nint> cadres = [];

    _ = Natives.EnumWindows(
        (fenetre, parametre) =>
        {
            _ = parametre;
            _ = Natives.GetWindowThreadProcessId(fenetre, out var processus);

            if (Nom(processus) == "DtHub" && Natives.IsWindowVisible(fenetre))
            {
                cadres.Add(fenetre);
            }

            return true;
        },
        0);

    foreach (var cadre in cadres)
    {
        List<string> logees = [];

        _ = Natives.EnumChildWindows(
            cadre,
            (enfant, _) =>
            {
                var filEnfant = Natives.GetWindowThreadProcessId(enfant, out var processus);

                if (Nom(processus) != "DtHub")
                {
                    // Visible ou non : l'onglet caché continue-t-il de coûter ?
                    var vue = Natives.IsWindowVisible(enfant) ? "VISIBLE" : "cachée ";

                    logees.Add($"      {vue}  {Decrire(enfant)}  fil={filEnfant}");
                }

                return true;
            },
            0);

        if (logees.Count == 0)
        {
            continue;
        }

        Console.WriteLine($"  logées sous {Decrire(cadre)}");

        foreach (var logee in logees)
        {
            Console.WriteLine(logee);
        }
    }
}

static GuiThreadInfo Info(uint fil)
{
    var info = default(GuiThreadInfo);
    info.cbSize = Marshal.SizeOf<GuiThreadInfo>();

    return Natives.GetGUIThreadInfo(fil, ref info) ? info : default;
}

static string Nom(uint processus)
{
    try
    {
        using var p = Process.GetProcessById((int)processus);
        return p.ProcessName;
    }
    catch (ArgumentException)
    {
        return "(fini)";
    }
    catch (InvalidOperationException)
    {
        return "(fini)";
    }
}

static string Decrire(nint fenetre)
{
    if (fenetre == 0)
    {
        return "(aucune)";
    }

    var processus = 0u;
    _ = Natives.GetWindowThreadProcessId(fenetre, out processus);

    var titre = Texte(Natives.GetWindowText, fenetre);
    var classe = Texte(Natives.GetClassName, fenetre);

    var taille = Natives.GetWindowRect(fenetre, out var cadre)
        ? string.Create(
            CultureInfo.InvariantCulture,
            $"{cadre.Right - cadre.Left}x{cadre.Bottom - cadre.Top}")
        : "?x?";

    return string.Create(
        CultureInfo.InvariantCulture,
        $"0x{fenetre:X}  {Nom(processus)}({processus})  {taille,9}  [{classe}]  « {titre} »");
}

static string Texte(Func<nint, char[], int, int> lire, nint fenetre)
{
    var tampon = new char[256];
    var longueur = lire(fenetre, tampon, tampon.Length);

    return longueur <= 0 ? string.Empty : new string(tampon, 0, longueur);
}

[StructLayout(LayoutKind.Sequential)]
internal struct Rectangle
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GuiThreadInfo
{
    public int cbSize;
    public int flags;
    public nint hwndActive;
    public nint hwndFocus;
    public nint hwndCapture;
    public nint hwndMenuOwner;
    public nint hwndMoveSize;
    public nint hwndCaret;
    public int left;
    public int top;
    public int right;
    public int bottom;
}

internal static class Natives
{
    internal delegate bool EnumProc(nint fenetre, nint parametre);

    [DllImport("user32.dll")]
    internal static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(nint fenetre, out uint processus);

    [DllImport("user32.dll")]
    internal static extern bool GetGUIThreadInfo(uint fil, ref GuiThreadInfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(nint fenetre, char[] texte, int capacite);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(nint fenetre, char[] texte, int capacite);

    [DllImport("user32.dll")]
    internal static extern bool IsWindowVisible(nint fenetre);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(nint fenetre, out Rectangle cadre);

    [DllImport("user32.dll")]
    internal static extern bool EnumWindows(EnumProc rappel, nint parametre);

    [DllImport("user32.dll")]
    internal static extern bool EnumChildWindows(nint parent, EnumProc rappel, nint parametre);

    /// <summary>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2.</summary>
    internal static readonly nint PerMonitorAwareV2 = -4;

    [DllImport("user32.dll")]
    internal static extern bool SetProcessDpiAwarenessContext(nint contexte);
}
