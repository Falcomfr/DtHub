# Feuille de route

Le versionnage suit [SemVer](https://semver.org/lang/fr/). Tant que la version
majeure est `0`, l'interface et les formats de configuration peuvent changer.

## v0.3 - Ce qui marche aujourd'hui

- Détection des téléphones en USB et en Wi-Fi, association assistée,
  reconnexion automatique même quand le port change après un redémarrage.
- Détection des instances du jeu, une par profil Android, quel que soit le
  numéro du profil.
- Ouverture de chaque instance dans sa fenêtre, sur son propre afficheur
  virtuel.
- Fenêtres superposées, ancrées sur une grille de neuf positions, ou réunies
  dans un cadre à onglets.
- Configurateur flottant à trois onglets, rappelé par raccourci.
- Palier de qualité réglable, global ou par compte, définition et débit
  compris.
- Reprise d'une fenêtre que la liaison a fait tomber.
- Bilan de l'appareil avant lancement : batterie, chaleur, place libre, bande
  Wi-Fi, préparation de l'économie d'énergie.
- Fenêtre de guides lisant papycha.fr, avec l'endroit où l'on s'est arrêté et
  ce que chaque quête débloque.
- Almanax du jour, lu sur le portail officiel et filtré sur DOFUS Touch.
- Temps de jeu par compte.
- Export et restauration des réglages.
- Rapport d'incident biffé de ce qui identifie, à copier et à envoyer.
- Publication en fichier unique, sans installateur, mise à jour depuis le
  dépôt, et intégration continue qui éprouve et livre.
- Essai sur deux téléphones réels, Android 11 et Android 16.

## v0.4 - Ce qui manque encore

- Indication de l'instance active à l'écran.
- Indexation du guide plus rapide : le filtre `modified_after` de l'API
  n'attaque que les pages touchées, et les donjons pèsent les quatre
  cinquièmes du temps. Voir D141.

## v1.0 - Stable

- Formats de configuration figés et migrations gérées.
- Documentation utilisateur finalisée.
- Diagnostic complet.

## En cours, hors code

- Publication du dépôt, page de présentation et première version téléchargeable.
- Signature Authenticode par SignPath Foundation, gratuite pour les projets
  libres, qui exige un dépôt public et une version déjà livrée. Voir
  `docs/CONFIANCE.md`.

## Hors périmètre, définitivement

Toute automatisation de jeu : robot, macro, répétition d'actions,
reconnaissance d'écran pour jouer, synchronisation d'entrées entre comptes.
Ce n'est pas une question de priorité, c'est une limite du projet.
