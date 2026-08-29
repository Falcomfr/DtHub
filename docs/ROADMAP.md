# Feuille de route

Le versionnage suit [SemVer](https://semver.org/lang/fr/). Tant que la version
majeure est `0`, l'interface et les formats de configuration peuvent changer.

## v0.1 - Ce qui marche aujourd'hui

- Détection des téléphones en USB et en Wi-Fi, association assistée,
  reconnexion automatique.
- Détection des instances du jeu, une par profil Android.
- Ouverture de chaque instance dans sa fenêtre, sur son propre afficheur
  virtuel.
- Fenêtres superposées, ancrées sur une grille de neuf positions.
- Configurateur flottant à trois onglets, rappelé par raccourci.
- Publication en fichier unique, sans installateur.

## v0.2 - Ce qui manque pour un usage quotidien confortable

- Essai complet sur plusieurs comptes et plusieurs téléphones.
- Réglages de mirroring exposés dans l'interface : images par seconde, débit,
  définition de l'écran virtuel.
- Options supplémentaires dans l'onglet Général, à définir à l'usage.
- Mémorisation de la position et de la taille du configurateur.

## v0.3 - Confort

- Ordre des instances modifiable, qui détermine l'ordre du parcours au clavier.
- Indication de l'instance active à l'écran.
- Reprise d'une instance dont la fenêtre a été fermée à la main.

## v1.0 - Stable

- Formats de configuration figés et migrations gérées.
- Documentation utilisateur finalisée.
- Diagnostic complet.

## Envisagé, sans engagement

- Icônes réelles des applications, si une voie raisonnable apparaît. Voir la
  décision D6.
- Signature Authenticode, si le programme est distribué au-delà d'un usage
  personnel.
- Intégration continue et publication automatique sur GitHub.

## Hors périmètre, définitivement

Toute automatisation de jeu : robot, macro, répétition d'actions,
reconnaissance d'écran pour jouer, synchronisation d'entrées entre comptes.
Ce n'est pas une question de priorité, c'est une limite du projet.
