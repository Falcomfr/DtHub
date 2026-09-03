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

**Les conventions d'écriture.** Identifiants en anglais, commentaires et
documentation en français, messages de commit en français à l'impératif. Un
commentaire dit *pourquoi*, jamais *quoi* : le code dit déjà ce qu'il fait.
Jamais de tiret cadratin.

**Les décisions se consignent.** Un choix d'architecture, un compromis, un
renoncement : cela va dans `docs/DECISIONS.md`, avec ce qui a été mesuré et ce
qui a été écarté. C'est la mémoire du projet et c'est ce qui évite de refaire
deux fois la même erreur.

## Signaler un problème

Le modèle d'incident guide le nécessaire. L'application sait composer un
rapport : le bouton « Signaler un problème » de l'onglet Fenêtres le met dans
le presse-papiers, déjà biffé de ce qui vous identifie. Collez-le tel quel.

Pour une faille, ne passez pas par une issue publique : voir `SECURITY.md`.
