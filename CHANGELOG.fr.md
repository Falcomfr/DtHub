# Journal des modifications

Traduction française de [CHANGELOG.md](CHANGELOG.md), version par version, à
partir de la 0.3.0. Chaque section est jointe à la livraison
sous le nom `notes.fr.md` : c'est elle que l'application affiche en français,
et elle qu'annonce le salon Discord.

Les rubriques suivent celles de l'anglais : `### Ajouté`, `### Modifié`,
`### Supprimé`, `### Corrigé`.

Une entrée tient en une ligne courte qui dit ce que le joueur y gagne ; le
pourquoi reste dans les commits et dans `docs/DECISIONS.md`.

## [Unreleased]

### Corrigé

- Dans le cadre à onglets, Ctrl+Tab change toujours de compte après un
  Alt+Tab pour y revenir.

## [0.7.3] - 2026-10-03

### Ajouté

- Le panneau dit quand le téléphone, son Wi-Fi ou le PC fait ramer le jeu,
  et quoi faire.

### Modifié

- Un téléphone n'affiche qu'une ligne de constats : le plus grave, avec le
  nombre des autres, tous visibles au survol.
- Les conseils Wi-Fi passent dans la case de liaison à côté du nom, qui ne
  jaunit plus que pour un souci en cours.

### Corrigé

- Un téléphone connecté à la fois par son nom et par son adresse n'affiche
  plus ses constats en double.

## [0.7.2] - 2026-10-02

### Modifié

- Une ligne de donjon commence par son niveau, finit par sa clé, et affiche
  de nouveau sa pierre d'âme, avec son icône.

### Corrigé

- Toutes les positions de donjon s'écrivent avec une virgule.

## [0.7.1] - 2026-10-02

### Modifié

- Les lignes des donjons sont allégées : l'en-tête de tranche dit la pierre
  d'âme commune, et une ligne n'affiche la taille que si elle diffère.
- Les boutons encadrés prennent un léger contour bleu au survol, le focus
  clavier se voit en anneau arrondi, et chaque bouton répond au clic.

### Corrigé

- Le niveau des donjons de niveau 200 n'est plus coupé.

## [0.7.0] - 2026-10-01

### Modifié

- Le signalement d'erreur donne l'adresse de la page après l'endroit de
  l'erreur, pour que Papycha l'ouvre directement.

### Corrigé

- La barre du haut de Papycha ne recouvre plus les guides ni le signalement
  d'erreur depuis la mise à jour d'octobre du site.

## [0.6.2] - 2026-09-30

### Corrigé

- Fermer la fenêtre à onglets n'oublie plus ses comptes ni sa place : tout
  revient au lancement suivant.

## [0.6.1] - 2026-09-28

### Corrigé

- Retirer une app ajoutée affiche un chargement au lieu de sembler ne rien
  faire.

## [0.6.0] - 2026-09-28

### Ajouté

- Option pour éteindre l'écran du téléphone pendant le jeu : il chauffe moins
  et la batterie tient plus.
- Le bouton + peut afficher n'importe quelle app déjà installée, comme un
  DOFUS Touch cloné, ou cloner le jeu sur un nouveau compte comme avant.
- Une app ajoutée ainsi se retire avec la corbeille de sa ligne.

### Modifié

- Les onglets prennent la couleur de leur compte.

## [0.5.2] - 2026-09-27

### Ajouté

- La ligne de chaque téléphone signale quand sa mémoire est pleine, le moment
  où les actions commencent à ramer.

## [0.5.1] - 2026-09-27

### Corrigé

- Après un Ctrl+Tab, un compte pouvait ne plus déplacer son personnage.

## [0.5.0] - 2026-09-27

### Ajouté

- Le panneau montre ce que fait chaque compte, et depuis combien de temps.
- Chaque téléphone affiche sa batterie et son Wi-Fi, et sa chauffe ou son
  espace libre quand ça compte.
- Chaque compte a une couleur, sur sa ligne, son onglet et sa fenêtre
  (Windows 11 pour la fenêtre).
- Un téléphone qui ne répond pas dit où regarder, avec les étapes pour sa
  marque.
- Un bouton en bas du panneau ouvre le Discord de DT Hub.

### Modifié

- Des lignes plus claires : lancer et arrêter au même endroit, les réglages du
  compte dans une seule pastille.

### Corrigé

- La langue choisie s'applique partout, et un téléphone n'apparaît plus deux
  fois.

## [0.4.0] - 2026-09-14

### Ajouté

- La distance en jeu se règle par compte, comme la qualité.

### Supprimé

- Le réglage « Journaliser les images par seconde ».

### Corrigé

- Les fenêtres de jeu ne restent plus noires, et le premier lancement ne
  demande plus un second clic.
- Un compte renommé garde son nom et sa place dans la liste.
- Un téléphone qui a changé d'adresse se reconnecte, et associer un nouveau
  téléphone refonctionne.
- Un lancement ou une action qui échoue est signalé et reste lisible.

### Modifié

- La liste des téléphones s'affiche plus vite.

## [0.3.0] - 2026-09-12

### Ajouté

- La fin d'une quête dit ce qu'elle débloque.

### Modifié

- Le panneau montre la batterie et le résumé de chaque téléphone, et le temps
  de jeu de la semaine par compte.
- Un palier de qualité par compte, avec ses propres images par seconde.
- Une souris simulée, en dernier recours pour les téléphones qui refusent les
  clics.
- Les réglages s'enregistrent et se restaurent.

### Corrigé

- L'application s'ouvre quatre fois et demie plus vite et ne plante plus au
  démarrage.
- Une connexion perdue ne ferme plus l'application.
- Fermer une fenêtre ferme aussi le jeu sur le téléphone.
- Le réglage de la langue fonctionne, et l'espagnol ne mélange plus tú et
  usted.
