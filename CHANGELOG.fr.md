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

- Le son du jeu ne se coupe plus sur un Wi-Fi chargé : il garde sa propre
  réserve, comme l'image.
- DT Hub consomme beaucoup moins de processeur quand le panneau est affiché,
  et presque rien quand il est réduit.
- Chaque fenêtre de jeu prend environ 160 Mo de mémoire de moins sur le
  téléphone.
- Un téléphone laissé de côté depuis des jours ne ralentit plus le panneau.

## [0.9.0] - 2026-10-17

### Ajouté

- Les quêtes répétables affichent leur rythme dans les listes des guides :
  quotidienne, hebdomadaire, répétable ou saisonnière.
- En plein écran, Échap en sort, et le panneau gagne un bouton plein écran.
- Chaque raccourci a une croix pour le laisser sans touche.

### Modifié

- Le plein écran passe en tête des raccourcis des fenêtres.
- Les intertitres de succès des guides n'affichent plus de fourchette de
  niveaux : le site en donne pour trop peu de quêtes.
- Les chemins de quêtes ouvrent la zone où ils mènent, et le chemin d'un
  donjon se range juste avant lui.

## [0.8.22] - 2026-10-16

### Modifié

- L'aide sur le Dossier sécurisé Samsung couvre aussi une connexion bloquée
  dans Island, et Parallel Space pour un compte de plus.

### Corrigé

- Sous Android 14 et moins, un clic dans un jeu dont le téléphone s'est
  endormi réveille le téléphone, et les clics suivants passent de nouveau.

## [0.8.21] - 2026-10-16

### Modifié

- Les raccourcis sont rangés en trois parties, et les comptes 1 à 8 comme les
  tailles 1 à 4 tiennent chacun sur une ligne qui se déplie.

## [0.8.20] - 2026-10-16

### Ajouté

- F1 à F8 mettent au premier plan le compte de cette ligne de la liste, dans
  son onglet s'il est dans le cadre. Sa touche s'affiche dans le titre de la
  fenêtre et à côté du compte. Modifiables dans Raccourcis.

### Corrigé

- Une erreur inattendue n'ouvre plus une nouvelle fenêtre toutes les demi-
  secondes : un seul signalement, et DT Hub reste utilisable derrière.
- Le temps de jeu compte maintenant les fenêtres fermées depuis DT Hub : croix
  d'un onglet, raccourci, Quitter.
- Une fenêtre fermée pendant son ouverture ne reste plus indiquée comme
  ouverte, et son jeu ne continue plus de tourner sur le téléphone.
- Un même compte ne peut plus s'ouvrir deux fois quand on le lance pendant le
  démarrage.
- Un téléphone lent à répondre ne ferme plus toutes les fenêtres de jeu.
- Fermer le cadre à onglets ne fait plus apparaître les onglets cachés, et un
  léger glissé sur la croix d'un onglet ferme le compte au lieu de déplacer
  l'onglet.
- Ranger les fenêtres ne donne plus le clavier à chaque fenêtre réduite.
- Une icône de compte abîmée ne provoque plus d'erreurs à répétition.
- Quitter ferme toujours DT Hub, et le relancer pendant sa fermeture ne le
  fait plus planter.
- Un réglage qui n'a pas pu être enregistré le dit maintenant dans le bandeau.
- Après un téléchargement trop long, la fenêtre des outils propose de nouveau
  « Réessayer » et « Continuer sans ».

## [0.8.19] - 2026-10-16

### Ajouté

- Chaque onglet porte une croix qui ferme son compte seul.

### Modifié

- Ctrl + E éteint l'écran du téléphone même quand « Éteindre l'écran du
  téléphone pendant le jeu » est décoché : celui du jeu au premier plan, ou
  tous les téléphones depuis une autre fenêtre DT Hub.
- « En grille » apparaît dès trois fenêtres de jeu.

### Corrigé

- Après « Éteindre », la ligne qui dit que l'écran du téléphone est allumé ne
  revient plus quelques secondes.

## [0.8.18] - 2026-10-15

### Ajouté

- Quand vous rallumez l'écran du téléphone pendant le jeu, pour un appel ou un
  message, une ligne sous le téléphone permet de le rééteindre, comme Ctrl + E
  depuis une fenêtre de jeu.
- « En grille » range quatre fenêtres de jeu sur les quatre quarts de
  l'écran, aussi avec Ctrl + G.
- Une option affiche la batterie, la chauffe, le lag et le temps de jeu dans
  le titre de chaque fenêtre de jeu.

## [0.8.17] - 2026-10-15

### Modifié

- Les fenêtres de jeu et les onglets affichent le nom du compte seul, sans
  « DT Hub » devant.

### Corrigé

- Dans un onglet, l'icône d'un compte sans couleur revient contre le bord
  gauche.

## [0.8.16] - 2026-10-15

### Ajouté

- « Toutes les versions », dans Nouveautés, ouvre les notes de chaque version
  sur GitHub, dans la langue de l'application.

### Modifié

- La case de compte « Corriger un jeu décalé » devient « Format du téléphone
  (si jeu décalé) », et son info-bulle dit de la laisser décochée dans la
  plupart des cas.

## [0.8.15] - 2026-10-15

### Corrigé

- Bitdefender ne signale plus les publicités de la carte affichée dans le
  Portail DT : leurs domaines sont désormais injoignables depuis toutes les
  fenêtres de DT Hub.
- Les champs de l'association montrent un exemple de ce qu'il faut saisir.

## [0.8.14] - 2026-10-15

### Ajouté

- Avec votre suivi Papycha relié, chaque zone de quêtes montre combien de
  quêtes sont faites sur son total, et combien sont en cours.
- Au survol, « À faire » et « Accessibles » disent ce que chaque filtre garde.

### Modifié

- Avec un filtre actif, le nombre à côté d'une zone est celui des quêtes qui
  restent affichées.
- Les zones de quêtes n'affichent plus de plages de niveaux.

## [0.8.13] - 2026-10-15

### Corrigé

- La fenêtre du Portail DT ne déclenche plus d'alerte Bitdefender : ses
  publicités sont refusées, comme sur la carte.
- Le niveau du personnage est aligné sur son nom dans les guides.

### Modifié

- « Où trouver ma phrase ? » mène directement à votre page de suivi Papycha
  et dit exactement quoi copier.

## [0.8.12] - 2026-10-15

### Corrigé

- Le plein écran (Ctrl+5) en mode onglets couvre tout l'écran, sans barre
  d'onglets ni bandes noires.
- La carte et DofusRoom s'ouvrent dans la langue de DT Hub et non plus dans
  celle de Windows ; les outils qui n'existent qu'en français le signalent.

### Ajouté

- Suivi des quêtes Papycha dans les guides : reliez votre suivi, une quête
  passe « en cours » quand vous suivez ses étapes, se marque terminée en un
  clic, et la liste montre l'état de chaque quête avec des filtres.
- « Mes quêtes » ouvre votre tableau de bord Papycha.
- Un bouton « Portail DT » ouvre lomitall.fr dans une fenêtre DT Hub.
- Un bouton à côté de la croix réduit le panneau ; Ctrl+P le fait revenir.
- La distance « Un peu éloignée », entre Éloignée et Normale.
- Au survol, une qualité ou une distance affiche ce qu'elle règle :
  définition, images par seconde, débit, densité.

### Modifié

- Le bouton Discord passe sur la ligne des outils DOFUS Touch.

## [0.8.11] - 2026-10-14

### Corrigé

- Le jeu remplit de nouveau l'écran du PC, comme avant la 0.8.10, qui lui
  donnait le format du téléphone avec des bandes autour.

### Ajouté

- « Corriger un jeu décalé », dans le menu d'un compte : pour un appareil qui
  dessine le jeu décalé ou coupé sur votre écran, comme une Galaxy Tab S10
  Ultra sur un écran ultra-large, le jeu garde le format de l'écran du
  téléphone ou de la tablette.
- Le panneau de DT Hub s'étire en hauteur par son bord bas.

## [0.8.10] - 2026-10-14

### Corrigé

- Le jeu s'affiche au format de l'écran du téléphone, quel que soit l'écran
  du PC : sur un écran ultra-large, il ne se dessine plus décalé avec son
  panneau coupé, comme sur une Galaxy Tab S10 Ultra. La fenêtre du jeu prend
  ce format elle aussi.

### Supprimé

- La relance automatique ajoutée en 0.8.9 : le défaut qu'elle visait n'était
  pas celui-là. Le rapport de diagnostic dit toujours comment le téléphone
  place chaque jeu.

## [0.8.9] - 2026-10-14

### Corrigé

- Un jeu que le téléphone laisse à la mauvaise taille dans sa fenêtre,
  décalé avec une bande noire et son panneau coupé, est relancé une fois en
  pleine taille.

### Modifié

- Le rapport de diagnostic dit comment le téléphone place chaque jeu ouvert,
  pour trouver ce qui cloche sur un appareil qu'on ne peut pas tester.

## [0.8.8] - 2026-10-13

### Corrigé

- DofusRoom : l'éditeur de carte colle sous la barre de titre, et la liste ne
  défile plus dans le vide quand une publicité reste vide.

### Modifié

- Le menu des outils dit à quoi sert DofusRoom : les lignes de vue des
  cartes de combat.

## [0.8.7] - 2026-10-13

### Ajouté

- DofusRoom rejoint les outils DOFUS Touch : les lignes de vue des cartes de
  combat Touch, dans une fenêtre à côté du jeu.
- Une alerte batterie se masque depuis sa page d'aide, sur un appareil qui
  n'a pas ce réglage, et se réaffiche depuis l'aide.
- Un téléphone sans le jeu peut quand même afficher ses autres applications,
  avec le « + ».

### Modifié

- L'aide pour un appareil qui reste hors ligne après l'association nomme les
  causes habituelles : bande Wi-Fi, réseau public, pare-feu, VPN.

## [0.8.6] - 2026-10-13

### Ajouté

- La qualité personnalisée propose 90 et 120 images par seconde, pour les
  téléphones à écran rapide.

## [0.8.5] - 2026-10-12

### Corrigé

- Changer d'onglet dans le cadre à onglets ne fait plus clignoter une partie
  de la fenêtre en blanc.

## [0.8.4] - 2026-10-12

### Modifié

- L'option « Image plus nette » n'augmente plus le débit Wi-Fi, ce qui
  faisait ramer le jeu avec plusieurs comptes ouverts.

### Corrigé

- Ctrl+5, pressé une deuxième fois, sort du plein écran et rend aux fenêtres
  leur taille et leur place.
- Le panneau des réglages ne peut plus être agrandi en le glissant en haut de
  l'écran, et un panneau resté agrandi rouvre à sa taille normale.
- Le cadre à onglets sort du plein écran à la taille qu'il avait.

## [0.8.3] - 2026-10-12

### Ajouté

- Une case « Image plus nette » sous les paliers de qualité rend le jeu au
  double de la taille de la fenêtre : le texte coloré, comme le rouge sur gris,
  devient aussi net que le blanc.

## [0.8.2] - 2026-10-11

### Corrigé

- Sur tablette, le jeu s'ouvre en plein écran dans sa fenêtre au lieu d'une
  fenêtre flottante à l'intérieur, et le tchat redevient visible.

## [0.8.1] - 2026-10-11

### Corrigé

- La densité de la qualité personnalisée arrive enfin au jeu : elle était
  ignorée pour tous les comptes. Sa liste affiche aussi sa valeur, se tape, et
  s'applique dès qu'on la choisit.
- Un réglage changé pendant la réouverture des fenêtres n'est plus perdu.
- La fenêtre Dofus Touch Map n'envoie plus ses fenêtres publicitaires au
  navigateur.
- Les lecteurs d'écran nomment les boutons à icône du panneau et lisent les
  choix de ses listes tels qu'ils s'affichent.

## [0.8.0] - 2026-10-11

### Ajouté

- Un menu d'outils DOFUS Touch dans l'onglet Appareils : Dofus Touch Map et
  Calcofus s'ouvrent dans une fenêtre de DT Hub, DTStuff et OcrApp
  s'installent et s'ouvrent depuis le PC, DofusBook s'ouvre dans le
  navigateur.
- Chaque ligne d'alerte a un lien Aide, adapté à l'application concernée et à
  la marque du téléphone.
- Un téléphone Honor ou Huawei branché mais pas vu reçoit les étapes de ses
  propres menus.
- Un pilote qui tient le débogage du téléphone sans le rendre à DT Hub est
  nommé, avec les étapes pour le remplacer.
- Un jeu rangé dans un Dossier sécurisé Samsung verrouillé est signalé, avec
  comment le déverrouiller et le garder ouvert.
- L'association sans fil accepte l'adresse tapée à la main quand le téléphone
  n'apparaît pas.
- La qualité personnalisée permet de régler la densité de l'écran du jeu :
  l'interface garde sa taille quand la fenêtre est plus petite.
- L'Almanax liste les offrandes de la semaine, chacune prête à copier pour la
  recherche de l'hôtel de vente, et l'offrande du jour se copie d'un clic.
- Chercher un métier dans l'Almanax, comme Mineur, liste ses jours de bonus
  sur les deux prochains mois, récolte et craft.
- L'Almanax signale les jours qui ont aussi une quête répétable spéciale ou
  une bénédiction, avec où aller.

### Modifié

- Un raccourci peut être une seule touche, comme Tab ; seules les touches qui
  écrivent dans le chat du jeu reçoivent un avertissement.
- Le clic droit fait Retour dans les fenêtres de jeu, comme dans scrcpy.
- La carte « Aucun appareil détecté » et le message de pilote manquant
  donnent la vraie cause et quoi faire.
- La fenêtre Dofus Touch Map se charge sans les publicités qui déclenchaient
  les antivirus, et propose d'ouvrir le site dans le navigateur ou de faire un
  don.
- Un Dossier sécurisé que Samsung ferme au PC est dit tel quel, et le conseil
  Samsung envoie désormais vers un profil de travail créé avec Island.

### Supprimé

- La ligne du profil Android sous un compte renommé.

### Corrigé

- Le panneau reste affiché après avoir quitté avec des comptes en onglets.

## [0.7.11] - 2026-10-10

### Modifié

- L'aide Samsung dit quoi faire quand le débogage USB est grisé par le
  bloqueur automatique.

### Corrigé

- Kaspersky supprimait DT Hub comme cheval de Troie : il cherche désormais
  ses mises à jour sans l'API de GitHub, et ne réécrit plus ses raccourcis
  quand ils sont déjà bons.

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
