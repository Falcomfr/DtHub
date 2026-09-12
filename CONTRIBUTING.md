# Contribuer

Merci de l'intérêt. Ce dépôt a des conventions inhabituelles et fermement
tenues : lisez `AGENTS.md` avant d'écrire une ligne. Il dit ce qui est interdit,
et pourquoi.

## Ce qui n'entrera jamais

**Toute automatisation de jeu.** Robot, macro, répétition d'actions,
reconnaissance d'écran pour jouer, synchronisation d'entrées entre comptes,
contournement d'une limitation du jeu. Une entrée utilisateur correspond à une
action, sur un compte, et à une seule. Ce n'est pas une question de priorité,
c'est la limite du projet, et une proposition dans ce sens sera close sans
discussion.

Sont également exclus : les crochets clavier de bas niveau, toute demande
d'élévation, toute manipulation de l'antivirus, et PowerShell, Node ou Python à
l'exécution de l'application.

## Avant d'ouvrir une demande de tirage

```
dotnet build DtHub.slnx -warnaserror     # zéro avertissement, c'est la cible
dotnet test DtHub.slnx                    # tout vert
```

La chaîne rejoue les deux. Une demande qui les casse ne sera pas relue.

**Écrivez une épreuve.** Elle doit nommer un comportement, pas une méthode :
`Un_mdns_muet_laisse_l_appairage_acquis_et_demande_le_port`, pas
`TestPairing2`. Les épreuves de ce dépôt se lisent comme une spécification, et
c'est ce qui les rend utiles.

**Les conventions d'écriture.** Identifiants en anglais. Un commentaire dit
*pourquoi*, jamais *quoi* : le code dit déjà ce qu'il fait. Jamais de tiret
cadratin.

**La langue, depuis la publication du dépôt.** La règle est simple : *ce qui ne
peut pas exister dans les deux langues s'écrit en anglais*. Un message de
commit, un commentaire de code, une note de version n'ont qu'une version ; ils
sont donc en anglais, à partir du 2026-09-12.

Ce qui existe déjà reste tel quel, et il faut le dire plutôt que de le cacher :

- les **291 commits** antérieurs sont en français, et ne peuvent plus changer
  sans réécrire un historique déjà public ;
- `docs/DECISIONS.md`, 392 Ko, reste en français. Sa valeur tient à sa
  précision, et une traduction en perdrait plus qu'elle n'apporterait ;
- les **commentaires existants** restent en français. Les nouveaux sont en
  anglais, et un fichier retouché en profondeur peut passer à l'anglais d'un
  bloc plutôt que ligne à ligne.

Le mélange se voit, et c'est le prix d'un choix fait après coup plutôt que
d'une réécriture de l'histoire.

**Les décisions se consignent.** Un choix d'architecture, un compromis, un
renoncement : cela va dans `docs/DECISIONS.md`, avec ce qui a été mesuré et ce
qui a été écarté. C'est la mémoire du projet et c'est ce qui évite de refaire
deux fois la même erreur.

## Signaler un problème

Le modèle d'incident guide le nécessaire. L'application sait composer un
rapport : le bouton « Signaler un problème » de l'onglet Fenêtres le met dans
le presse-papiers, déjà biffé de ce qui vous identifie. Collez-le tel quel.

Pour une faille, ne passez pas par une issue publique : voir `SECURITY.md`.
