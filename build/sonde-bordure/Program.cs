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

// Sans cela, GetWindowRect et GetPixel ne parlent pas de la meme
// chose sur un ecran a l'echelle : les coordonnees rendues sont
// virtualisees et l'echantillon tombe a cote de la fenetre.
_ = SetProcessDpiAwarenessContext(-4);

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

var avant = Echantillon(fenetre);

var bordure = Teindre(fenetre, BorderColour, Essai);
var barre = Teindre(fenetre, CaptionColour, Essai);

Console.WriteLine($"  DWMWA_BORDER_COLOR  -> {Verdict(bordure)}");
Console.WriteLine($"  DWMWA_CAPTION_COLOR -> {Verdict(barre)}");
Console.WriteLine();

// Le temps que la composition rattrape.
Thread.Sleep(900);

var apres = Echantillon(fenetre);

Console.WriteLine("  cadre, avant -> apres :");

// Quatre points pris sur le cadre lui-meme, a un pixel du bord, la ou
// DWM dessine : haut et bas au milieu, gauche et droite a mi-hauteur.
string[] coins = ["bord haut", "bord bas", "bord gauche", "bord droit"];

var change = 0;

for (var i = 0; i < avant.Length; i++)
{
    var bouge = avant[i] != apres[i];

    if (bouge)
    {
        change++;
    }

    Console.WriteLine(
        $"    {coins[i],-14} {Ecrire(avant[i])} -> {Ecrire(apres[i])}{(bouge ? "   change" : string.Empty)}");
}

Console.WriteLine();
Console.WriteLine(change > 0
    ? $"**Le cadre a change sur {change} des {avant.Length} points.** DWM peint bien une fenetre voisine."
    : "**Aucun pixel du cadre n'a bouge.** L'appel est accepte et ne peint rien de visible.");
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

static uint[] Echantillon(nint fenetre)
{
    if (!GetWindowRect(fenetre, out var r))
    {
        return [0, 0, 0, 0];
    }

    var ecran = GetDC(nint.Zero);

    try
    {
        var cx = (r.Left + r.Right) / 2;
        var cy = (r.Top + r.Bottom) / 2;

        return
        [
            GetPixel(ecran, cx, r.Top),
            GetPixel(ecran, cx, r.Bottom - 1),
            GetPixel(ecran, r.Left, cy),
            GetPixel(ecran, r.Right - 1, cy),
        ];
    }
    finally
    {
        _ = ReleaseDC(nint.Zero, ecran);
    }
}

static string Ecrire(uint colorref) => colorref == 0xFFFFFFFF
    ? "illisible"
    : $"R{colorref & 0xFF:X2} V{(colorref >> 8) & 0xFF:X2} B{(colorref >> 16) & 0xFF:X2}";

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

[DllImport("user32.dll")]
static extern bool GetWindowRect(nint window, out Rect rect);

[DllImport("user32.dll")]
static extern nint GetDC(nint window);

[DllImport("user32.dll")]
static extern int ReleaseDC(nint window, nint dc);

[DllImport("user32.dll")]
static extern nint SetProcessDpiAwarenessContext(nint context);

[DllImport("gdi32.dll")]
static extern uint GetPixel(nint dc, int x, int y);

[StructLayout(LayoutKind.Sequential)]
struct Rect
{
    public int Left, Top, Right, Bottom;
}

delegate bool EnumWindowsProc(nint window, nint parameter);
