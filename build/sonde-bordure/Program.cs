// Sonde : peut-on teinter le cadre d'une fenêtre qui appartient à un
// autre processus ?
//
// La question décide d'une fonctionnalité entière. Les fenêtres de jeu
// sont créées par scrcpy, jamais par nous. L'application les déplace,
// les redimensionne et les renomme déjà à travers user32, donc entrer
// dans ces fenêtres est une pratique établie ici. Reste à savoir si
// DWM accepte de teindre leur cadre depuis l'extérieur : la
// documentation ne le dit pas, et tous les usages qu'on peut citer sont
// intra-processus.
//
// Lecture seule pour le jeu : rien n'est écrit, rien n'est tué, et la
// teinte est rendue avant de sortir.
//
// Usage :
//   sonde-bordure                 liste les fenêtres visibles
//   sonde-bordure <handle>        teinte celle-là, attend, puis rend

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

const int BorderColour = 34;
const int CaptionColour = 35;
const uint ColourDefault = 0xFFFFFFFF;

// Un vert bien à lui, qu'on ne confondra avec aucun thème Windows.
// COLORREF est 0x00BBGGRR, et pas RGB : se tromper d'ordre donne une
// couleur plausible, ce qui est le pire des cas pour une sonde.
const uint Essai = 0x00888D3F;

if (!OperatingSystem.IsWindows())
{
    Console.WriteLine("Windows seulement.");
    return 1;
}

Console.WriteLine($"Windows {Environment.OSVersion.Version}");
Console.WriteLine(
    Environment.OSVersion.Version.Build >= 22000
        ? "build >= 22000 : les attributs 34 et 35 existent."
        : "build < 22000 : les attributs 34 et 35 n'existent pas, E_INVALIDARG attendu.");
Console.WriteLine();

if (args.Length == 0)
{
    Lister();
    Console.WriteLine();
    Console.WriteLine("Relancer avec un handle pour teinter cette fenêtre.");
    return 0;
}

if (!long.TryParse(args[0], out var brut))
{
    Console.WriteLine($"Handle illisible : {args[0]}");
    return 2;
}

var fenetre = (nint)brut;

Console.WriteLine($"cible : {Decrire(fenetre)}");
Console.WriteLine();

var bordure = Teindre(fenetre, BorderColour, Essai);
var barre = Teindre(fenetre, CaptionColour, Essai);

Console.WriteLine($"  DWMWA_BORDER_COLOR  -> {Verdict(bordure)}");
Console.WriteLine($"  DWMWA_CAPTION_COLOR -> {Verdict(barre)}");
Console.WriteLine();

if (bordure != 0 && barre != 0)
{
    Console.WriteLine("Les deux ont été refusés : la surface 3 n'existe pas sur cette machine.");
    return 0;
}

Console.WriteLine("Les appels ont été acceptés. **Regarder la fenêtre maintenant.**");
Console.WriteLine("Un code de retour nul ne prouve pas que le cadre a changé : c'est");
Console.WriteLine("l'oeil qui tranche, et c'est tout l'objet de cette sonde.");
Console.WriteLine();
Console.WriteLine("Entrée pour rendre la teinte d'origine.");
_ = Console.ReadLine();

_ = Teindre(fenetre, BorderColour, ColourDefault);
_ = Teindre(fenetre, CaptionColour, ColourDefault);

Console.WriteLine("rendu.");
return 0;

static int Teindre(nint fenetre, int attribut, uint couleur) =>
    DwmSetWindowAttribute(fenetre, attribut, ref couleur, sizeof(uint));

static string Verdict(int hresult) => hresult switch
{
    0 => "accepté (S_OK)",
    unchecked((int)0x80070057) => "refusé (E_INVALIDARG, attribut inconnu de ce Windows)",
    unchecked((int)0x80070005) => "refusé (E_ACCESSDENIED, le processus voisin est protégé)",
    unchecked((int)0x80070578) => "refusé (handle de fenêtre invalide)",
    _ => $"refusé (0x{hresult:X8})",
};

static void Lister()
{
    Console.WriteLine("fenêtres visibles et titrées :");

    _ = EnumWindows(
        (fenetre, _) =>
        {
            if (IsWindowVisible(fenetre) && Titre(fenetre).Length > 0)
            {
                Console.WriteLine($"  {Decrire(fenetre)}");
            }

            return true;
        },
        nint.Zero);
}

static string Decrire(nint fenetre)
{
    _ = GetWindowThreadProcessId(fenetre, out var processus);

    var nom = "?";

    try
    {
        nom = Process.GetProcessById((int)processus).ProcessName;
    }
    catch (ArgumentException)
    {
        // Le processus est parti entre l'énumération et la lecture.
        // Rien à en dire, et rien qui empêche d'imprimer la ligne.
    }

    return $"{(long)fenetre,-12} {nom,-14} {Titre(fenetre)}";
}

static string Titre(nint fenetre)
{
    var tampon = new StringBuilder(256);

    return GetWindowText(fenetre, tampon, tampon.Capacity) > 0 ? tampon.ToString() : string.Empty;
}

[DllImport("dwmapi.dll")]
static extern int DwmSetWindowAttribute(nint window, int attribute, ref uint value, int size);

[DllImport("user32.dll")]
static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

[DllImport("user32.dll")]
static extern bool IsWindowVisible(nint window);

[DllImport("user32.dll", CharSet = CharSet.Unicode)]
static extern int GetWindowText(nint window, StringBuilder text, int count);

[DllImport("user32.dll")]
static extern uint GetWindowThreadProcessId(nint window, out uint processId);

delegate bool EnumWindowsProc(nint window, nint parameter);
