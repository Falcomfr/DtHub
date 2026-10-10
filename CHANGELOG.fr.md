# Journal des modifications

Traduction française de [CHANGELOG.md](CHANGELOG.md), version par version, à
partir de la 0.3.0. Chaque section est jointe à la livraison
sous le nom `notes.fr.md` : c'est elle que l'application affiche en français,
et elle qu'annonce le salon Discord.

Les rubriques suivent celles de l'anglais : `### Ajouté`, `### Modifié`,
`### Supprimé`, `### Corrigé`.

Une entrée tient en une ligne courte qui dit ce que le joueur y gagne ; le
pourquoi reste dans les commits et dans `docs/DECISIONS.md`.

## [0.7.10] - 2026-10-10

### Modifié

- L'aide du bouton d'association et la confirmation de suppression précisent
  que l'association ne sert qu'au sans-fil, et comment retrouver un appareil
  supprimé.

### Corrigé

- Un appareil supprimé revient dès que son câble USB est rebranché.
- La puce de réglages et les boutons de chaque compte restent alignés d'une
  ligne à l'autre, quelle que soit la distance ou la qualité affichée, et à
  côté d'une application ajoutée.
- Les boutons de droite ne se décalent plus quand la liste des appareils se
  met à défiler.

## [0.7.9] - 2026-10-04

### Ajouté

- La fenêtre de rapport de problème mène aussi au serveur Discord, rapport
  copié, pour y trouver de l'aide sans compte GitHub.

### Corrigé

- Dans un onglet, é, à, ? et les autres touches qui ne sont pas des lettres
  écrivent de nouveau dans le jeu.

## [0.7.8] - 2026-10-04

### Modifié

- Une alerte de lag s'affiche en une minute environ au lieu de deux, et les
  alertes de chaleur et de mémoire se rafraîchissent toutes les trente
  secondes.

### Corrigé

- Un téléphone qui décroche du Wi-Fi quitte le panneau en une seconde
  environ, et non plus en plusieurs.
- Un téléphone branché pendant un balayage n'attend plus le suivant pour
  apparaître.

## [0.7.7] - 2026-10-03

### Ajouté

- Le titre du cadre à onglets rappelle les raccourcis, comme les fenêtres
  libres.
- Six nouvelles couleurs de compte, une teinte claire pour chacune des six.

### Modifié

- Une fenêtre ne porte plus la couleur de son compte que sur son liseré,
  adouci, dans le cadre comme en fenêtre libre ; la barre de titre reste
  celle de Windows.
- Les onglets perdent leur fond teinté, et l'onglet actif est souligné à la
  couleur de son compte.
- Les couleurs de compte sont plus vives.

### Corrigé

- Un compte sorti du cadre retrouve la couleur de son liseré.

## [0.7.6] - 2026-10-03

### Corrigé

- Après un Alt+Tab, le clavier arrive toujours au jeu : une fenêtre de jeu
  pouvait croire Alt encore enfoncé et garder toutes les touches.

## [0.7.5] - 2026-10-03

### Modifié

- La qualité moyenne passe à 30 images par seconde : environ un demi-cœur
  de moins sur le téléphone, et un débit divisé par deux.

### Corrigé

- Un téléphone qui ralentit parce que sa coque est trop chaude est
  désormais signalé, sur les téléphones comme les Xiaomi qui ne le disent
  que par cette sonde.
- Ctrl+Tab tapé dans les guides, le panneau ou l'almanax ramène le cadre à
  onglets et change de compte.

## [0.7.4] - 2026-10-03

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
