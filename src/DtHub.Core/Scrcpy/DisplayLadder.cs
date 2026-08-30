namespace DtHub.Core.Scrcpy;

/// <summary>
/// Définition d'afficheur virtuel adaptée à la taille d'une fenêtre.
///
/// L'image est mise à l'échelle de la fenêtre. Un afficheur toujours pris à la
/// définition de l'écran rendrait donc l'interface du jeu minuscule dans une
/// petite fenêtre, réduite d'autant, et un afficheur toujours petit la rendrait
/// énorme et floue en grand. La définition suit la fenêtre.
///
/// Par paliers, et non au pixel près : la définition est figée à l'ouverture de
/// la session, et une échelle qui change à chaque relance serait déroutante.
/// Le palier retenu est le premier au-dessus de la fenêtre, si bien que l'image
/// est toujours réduite, jamais agrandie, et reste nette.
/// </summary>
public static class DisplayLadder
{
    /// <summary>
    /// Hauteurs proposées. L'écart d'un palier au suivant vaut un tiers de
    /// palier au plus : le saut d'échelle d'une relance à l'autre reste petit.
    /// </summary>
    private static readonly int[] Heights =
        [540, 720, 900, 1080, 1260, 1440, 1620, 1800, 1980, 2160];

    /// <summary>Définition de repli quand l'écran n'est pas connu.</summary>
    public const int FallbackWidth = 1920;

    /// <summary>
    /// Hauteur qu'aucun encodeur ne refuse.
    ///
    /// Les encodeurs vidéo annoncent une définition maximale, et elle varie
    /// d'un appareil à l'autre : 7680x4320 sur un Xiaomi 13T, mais seulement
    /// 1920x1088 sur bien des appareils d'entrée de gamme ou plus anciens.
    /// Au-delà, la session est refusée. C'est la seule dépendance au matériel
    /// de tout le mécanisme, et elle sert de repli.
    /// </summary>
    public const int FallbackHeight = 1080;

    /// <summary>
    /// Définition à demander pour une fenêtre dont la zone client fait la
    /// hauteur donnée, sur un écran donné.
    ///
    /// Le rapport est celui de l'écran : c'est lui que la fenêtre garde, et
    /// s'en écarter laisserait une bande.
    /// </summary>
    public static (int Width, int Height) For(int clientHeight, int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return (FallbackWidth, FallbackHeight);
        }

        var aspect = (double)screenWidth / screenHeight;
        var height = Choose(clientHeight, screenHeight);

        return (Even((int)Math.Round(height * aspect)), Even(height));
    }

    /// <summary>
    /// Premier palier au-dessus de la fenêtre, sans jamais dépasser l'écran :
    /// au-delà, l'encodeur du téléphone travaillerait pour des pixels que
    /// personne ne verrait.
    /// </summary>
    private static int Choose(int clientHeight, int screenHeight)
    {
        foreach (var step in Heights)
        {
            if (step >= screenHeight)
            {
                break;
            }

            if (step >= clientHeight)
            {
                return step;
            }
        }

        return screenHeight;
    }

    /// <summary>Les encodeurs vidéo refusent les côtés impairs.</summary>
    private static int Even(int value) => Math.Max(2, value - (value % 2));
}
