// Development probe. Never used by the application.
//
// It resolves the two unknowns of the Almanax window:
//
//   1. does a zero-height WebView2 load the page correctly, and does
//      its script run? The window does not show the page, it reads
//      it;
//   2. do the bridge's landmarks hold up on the real page?
//
// It returns zero if the reading succeeds and the block read is
// indeed that of DOFUS Touch, one otherwise.
//
// To run: dotnet run --project build/sonde-almanax -- [yyyy-mm-dd]
using System.Globalization;
using System.IO;
using System.Windows;

using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

var jour = args.Length > 0 && DateOnly.TryParse(args[0], CultureInfo.InvariantCulture, out var lu)
    ? lu
    : DateOnly.FromDateTime(DateTime.Now);

var adresse = "https://www.krosmoz.com/fr/almanax/"
    + jour.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
    + "?game=dofustouch";

var pont = File.ReadAllText(Path.Combine(
    AppContext.BaseDirectory, "..", "..", "..", "..", "..",
    "src", "DtHub.App", "Assets", "almanax-bridge.js"));

// Top-level statements do not carry [STAThread], and WPF requires it.
var code = 1;

var fil = new Thread(() => code = Lire(adresse, pont));

fil.SetApartmentState(ApartmentState.STA);
fil.Start();
fil.Join();

return code;

static int Lire(string adresse, string pont)
{
var resultat = 1;
var application = new Application();
var vue = new WebView2();

// The same layout as the window: the reader has no height.
var fenetre = new Window
{
    Title = "sonde-almanax",
    Width = 400,
    Height = 120,
    Content = vue,
};

vue.Height = 0;

fenetre.Loaded += async (_, _) =>
{
    try
    {
        var dossier = Path.Combine(Path.GetTempPath(), "dthub-sonde-almanax");

        await vue.EnsureCoreWebView2Async(
            await CoreWebView2Environment.CreateAsync(userDataFolder: dossier)).ConfigureAwait(true);

        await vue.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(pont).ConfigureAwait(true);

        vue.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            var charge = e.TryGetWebMessageAsString();

            Console.WriteLine(charge);

            resultat = charge.Contains("DOFUS Touch", StringComparison.OrdinalIgnoreCase) ? 0 : 1;

            application.Shutdown();
        };

        vue.CoreWebView2.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
            {
                Console.WriteLine($"navigation en échec : {e.WebErrorStatus}");
                application.Shutdown();
            }
        };

        Console.WriteLine($"lecture de {adresse}");

        vue.CoreWebView2.Navigate(adresse);
    }
    catch (Exception erreur)
    {
        Console.WriteLine($"le moteur n'a pas démarré : {erreur.Message}");
        application.Shutdown();
    }
};

// A safeguard: without it, a page that posts nothing leaves the
// probe open.
var minuteur = new System.Windows.Threading.DispatcherTimer
{
    Interval = TimeSpan.FromSeconds(40),
};

minuteur.Tick += (_, _) =>
{
    Console.WriteLine("rien n'a été posté en quarante secondes.");
    application.Shutdown();
};

minuteur.Start();

application.Run(fenetre);

return resultat;
}
