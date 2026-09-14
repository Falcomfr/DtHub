using DtHub.Core.Papycha;

namespace DtHub.Tests.Papycha;

/// <summary>
/// Le compte au pied d'un guide : celui des quêtes du succès, telles
/// que la liste les montre. D159 avait fait gagner le site, qui
/// compte autre chose.
/// </summary>
public sealed class QuestProgressTests
{
    private static QuestNeighbours Succes(int rang, int total) =>
        new(null, null, rang, total);

    [Fact]
    public void Le_compte_est_celui_des_quetes_du_succes()
    {
        // « Devenir une légende (2) » dans la liste, deux quêtes
        // dedans : la première se lit 1 / 2. Le site, lui, annonce
        // « Étape 10/12 » pour son propre succès de douze quêtes,
        // dont dix ne sont pas au catalogue.
        Assert.Equal((1, 2), QuestProgress.Of(Succes(1, 2)));
        Assert.Equal((2, 2), QuestProgress.Of(Succes(2, 2)));
    }

    [Fact]
    public void Un_succes_de_trois_quetes_se_lit_trois_sur_trois()
    {
        Assert.Equal((3, 3), QuestProgress.Of(Succes(3, 3)));
    }

    [Fact]
    public void Une_quete_sans_succes_est_une_sur_une()
    {
        // Elle ne montrait rien du tout, et c'est ce qui a lancé
        // toute l'affaire : on peut toujours passer à la suivante,
        // le compte dit seulement ce que la suite contient.
        Assert.Equal((1, 1), QuestProgress.Of(default));
    }

    [Fact]
    public void Une_quete_que_le_catalogue_ignore_est_une_sur_une()
    {
        Assert.Equal((1, 1), QuestProgress.Of(Succes(0, 0)));
    }

    [Fact]
    public void Un_rang_perdu_dans_un_succes_connu_retombe_sur_une_sur_une()
    {
        // Le rang vaut zéro quand la quête ouverte n'a pas été
        // retrouvée dans son groupe. Afficher « 0 / 4 » serait pire
        // que de ne rien promettre.
        Assert.Equal((1, 1), QuestProgress.Of(Succes(0, 4)));
    }

    [Fact]
    public void Le_compte_ne_suit_jamais_la_quete_suivante_hors_succes()
    {
        // La suivante peut sortir du succès, et le bouton y mène :
        // le total ne bouge pas pour autant.
        var derniere = new QuestNeighbours(null, null, 2, 2);

        Assert.Equal((2, 2), QuestProgress.Of(derniere));
    }
}
