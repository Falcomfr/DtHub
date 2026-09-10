namespace DtHub.Core.Devices;

/// <summary>
/// Combien de millisecondes retenir une image avant de l'afficher.
///
/// scrcpy affiche chaque image dès qu'elle arrive. C'est le bon choix quand
/// elles arrivent régulièrement : la latence est alors au plus court. Mais
/// quand la liaison hoquette, l'image se fige le temps du hoquet, et le jeu
/// paraît ramer même si le débit suffit largement.
///
/// Mesuré sur le poste, deux fenêtres ouvertes, écran de jeu immobile, à peine
/// quatre mégabits sur une liaison qui en porte huit à treize : la latence
/// allait de 4 ms à **223 ms**, pour 39 ms de moyenne. Rien n'était saturé, ni
/// le PC, dont le processeur ne faisait rien, ni la bande passante. C'est la
/// seule irrégularité de la liaison qui se voyait.
///
/// Un tampon échange cette irrégularité contre un retard constant. Le marché
/// est bon tant que le retard reste sous le seuil où le clic paraît mou.
/// </summary>
public static class VideoBuffer
{
    /// <summary>
    /// Rien à compenser : c'est ce que rend une liaison filaire.
    ///
    /// L'USB n'a ni voisin ni interférence, et sa latence brute est un cadeau
    /// qu'on ne gâche pas en retenant les images pour rien.
    /// </summary>
    public const int None = 0;

    /// <summary>
    /// Plafond assumé.
    ///
    /// Le tampon vise la gigue **moyenne**, pas les pointes. Sur la liaison
    /// mesurée, la moyenne était de 39 ms et le pire de 223 : couvrir le pire
    /// aurait demandé un quart de seconde de retard sur chaque clic, ce qui
    /// remplace une gêne par une autre, en pire. Un premier essai à
    /// quatre-vingt-dix millisecondes s'est d'ailleurs fait reprocher sa
    /// mollesse avant même d'être atteint.
    /// </summary>
    public const int Ceiling = 60;

    /// <summary>
    /// Le tampon qui convient à une liaison, en millisecondes.
    ///
    /// La qualité de la liaison se lit sur deux choses que l'appareil dit déjà :
    /// la puissance reçue, et la part de trames qu'il a fallu réémettre. La
    /// bande compte aussi, la 2,4 GHz étant partagée avec tout le voisinage
    /// là où la 5 GHz est presque toujours tranquille.
    ///
    /// Les paliers sont grossiers, et c'est voulu : un tampon n'a pas besoin
    /// d'être juste au millième, il a besoin d'être du bon ordre de grandeur.
    /// </summary>
    /// <param name="link">Ce que l'appareil dit de sa liaison, ou null en USB.</param>
    public static int MillisecondsFor(WifiLink? link)
    {
        if (link is null)
        {
            return None;
        }

        // Une liaison de 5 GHz confortable ne hoquette pas assez pour mériter
        // un retard : quelques images d'avance suffisent à absorber le reste.
        var baseline = link.Is24GHz ? 25 : 10;

        var weak = link.Rssi switch
        {
            >= -55 => 0,
            >= -60 => 5,
            >= -65 => 12,
            >= -70 => 20,
            _ => 30,
        };

        // Les réémissions disent le voisinage : elles montent quand le canal
        // est disputé, ce que la seule puissance reçue ne montre pas.
        var crowded = link.RetryShare switch
        {
            >= 0.20 => 10,
            >= 0.10 => 5,
            _ => 0,
        };

        return Math.Min(Ceiling, baseline + weak + crowded);
    }
}
