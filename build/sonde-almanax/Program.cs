// Sonde de développement. Jamais employée par l'application.
//
// Elle lève les deux inconnues de la fenêtre de l'Almanax :
//
//   1. un WebView2 de hauteur nulle charge-t-il bien la page, et son script
//      s'exécute-t-il ? La fenêtre ne montre pas la page, elle la lit ;
//   2. les repères du pont tiennent-ils sur la page réelle ?
//
// Elle rend zéro si la lecture aboutit et que le bloc lu est bien celui de
// DOFUS Touch, un sinon.
//
// À lancer : dotnet run --project build/sonde-almanax -- [aaaa-mm-jj]
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

// Les instructions de haut niveau ne portent pas [STAThread], et WPF l'exige.
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

// La même mise en page que la fenêtre : le lecteur n'a pas de hauteur.
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

// Un garde-fou : sans lui, une page qui ne poste rien laisse la sonde ouverte.
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
