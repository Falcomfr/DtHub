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
    /// Hauteur que la plupart des encodeurs acceptent.
    ///
    /// Les encodeurs vidéo annoncent une définition maximale, et elle varie
    /// d'un appareil à l'autre : 7680x4320 sur un Xiaomi 13T, mais seulement
    /// 1920x1088 sur bien des appareils d'entrée de gamme ou plus anciens.
    /// Au-delà, la session est refusée. C'est la seule dépendance au matériel
    /// de tout le mécanisme, et elle sert de repli.
    /// </summary>
    public const int FallbackHeight = 1080;

    /// <summary>
    /// Hauteurs à retenter après un refus, de la plus généreuse à la plus sûre.
    ///
    /// Un seul repli ne suffit pas. 1080 couvre les encodeurs plafonnés à
    /// 1920x1088, qui sont le cas courant, mais pas ceux plafonnés à 1280x720,
    /// que l'on trouve sur le bas de gamme ancien et sur les tablettes
    /// d'entrée de gamme. Avec un repli unique à 1080, ces appareils échouaient
    /// sans qu'on retente jamais rien.
    ///
    /// Deux essais au plus : chaque tentative coûte l'attente complète, et
    /// descendre plus bas que 720 donnerait une image que personne ne veut.
    /// </summary>
    public static readonly int[] FallbackHeights = [1080, 720];

    /// <summary>
    /// Définition de repli sous une hauteur donnée, au rapport d'image demandé,
    /// ou <c>null</c> s'il n'y a plus rien de plus modeste à tenter.
    ///
    /// Le rapport est conservé. Le repli imposait auparavant du 16:9, si bien
    /// que la fenêtre changeait de forme entre la première tentative et la
    /// seconde sur un écran 21:9 ou 16:10.
    /// </summary>
    public static (int Width, int Height)? Below(int height, int aspectWidth, int aspectHeight)
    {
        foreach (var step in FallbackHeights)
        {
            if (step < height)
            {
                return At(step, aspectWidth, aspectHeight);
            }
        }

        return null;
    }

    /// <summary>
    /// Définition d'une hauteur donnée, au rapport d'image demandé. Un rapport
    /// inconnu retombe sur le 16:9 de la définition de repli.
    /// </summary>
    public static (int Width, int Height) At(int height, int aspectWidth, int aspectHeight)
    {
        if (aspectWidth <= 0 || aspectHeight <= 0)
        {
            return (Even(height * FallbackWidth / FallbackHeight), Even(height));
        }

        var aspect = (double)aspectWidth / aspectHeight;

        return (Even((int)Math.Round(height * aspect)), Even(height));
    }

    /// <summary>
    /// Définition à demander pour une fenêtre dont la zone client fait la
    /// hauteur donnée, sur un écran donné.
    ///
    /// Le rapport est celui de l'écran : c'est lui que la fenêtre garde, et
    /// s'en écarter laisserait une bande. La qualité choisie peut borner la
    /// hauteur, ce qui allège l'encodeur du téléphone.
    /// </summary>
    public static (int Width, int Height) For(
        int clientHeight,
        int screenWidth,
        int screenHeight,
        int maximumHeight = int.MaxValue)
    {
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return (FallbackWidth, FallbackHeight);
        }

        var aspect = (double)screenWidth / screenHeight;
        var height = Math.Min(Choose(clientHeight, screenHeight), Math.Max(360, maximumHeight));

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
