# Changelog

Toutes les modifications notables de ce projet sont consignées ici.

Le format suit [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/)
et le projet respecte le [versionnage sémantique](https://semver.org/lang/fr/).

## [Non publié]

### Ajouté

- La branche « Donjons » s'ouvre : les 83 donjons du site, rangés par palier de
  cinquante niveaux, avec leur niveau entre parenthèses et, à droite, la clef
  exigée, la taille de la pierre d'âme et la position. Le nom de la clef vient
  au survol.
- Une page de donjon se parcourt par ses sections — Monstres, Liste des salles,
  Boss, Mécanique du donjon, Les succès, Fin du donjon — et non par un résumé de
  paragraphe : ce n'est pas une suite de consignes mais un dossier.
- La recherche rend un quatrième groupe, « Donjons ».
- Deux sections de plus à la racine : « Raids » et « Tanières », avec leur
  niveau entre parenthèses et le classement par niveau. Le site ne le met dans
  ses métadonnées que pour une des dix : les neuf autres l'écrivent en clair
  dans leur première ligne, où il est désormais lu.
- Les chemins entrent dans la fenêtre, rangés dans la branche qu'ils servent :
  six sous « Donjons », quinze sous « Zone de Quêtes », chacun dans une
  sous-branche « Chemins ». Un chemin va aux donjons s'il écrit le mot
  « donjon » ou s'il partage au moins deux mots distinctifs avec un donjon du
  catalogue ; un seul mot commun ne suffit pas, faute de quoi le zaap de la
  canopée passerait pour le chemin de la Canopée du Kimbo.
- Un chemin se parcourt par ses étapes, « Jusqu'à la première grotte » à « Fin
  du chemin » : la règle qui distingue le dossier de la consigne ne regarde plus
  le bloc des donjons mais la présence de titres de sections.
- Le pied de succès disparaît quand il n'a rien à dire. Un donjon, un raid, une
  tanière et un chemin n'appartiennent à aucune suite : ils n'ont ni quête
  avant, ni quête après, ni rang dans un succès, et le pied ne montrait pour eux
  qu'un filet et une bande vide au-dessus de la source.
- Les tanières et les raids se parcourent enfin par leurs sections. Une page se
  lit par ses titres de second rang, mais les deux raids n'ont d'autre titre que
  « Sommaire » et sept tanières sur huit descendent les leurs au quatrième
  rang : on lit alors le sommaire que la page se donne. La tanière du Piou passe
  de une étape à sept, le Domaine du Dark Vlad de une à quatre. Rien ne change
  pour les donjons, les chemins et les quêtes, aucun des sept cent
  quatre-vingt-deux guides ne portant de sommaire.
- Trois des quarante liens de ces sommaires visent une ancre qui n'existe pas,
  « #salles » pour « salle ». Le texte du lien la retrouve, comparé aux
  identifiants de la page sans accents ni article.
- Le niveau d'un donjon se dit « niv. 100 » et non plus « 100 » : le nombre entre
  parenthèses se confondait avec ceux qui comptent les quêtes d'une zone.
- Le rond d'attente se pose en haut de la page et non plus en son milieu, où il
  tombait à cinq cents pixels sous le bandeau, dans une étendue vide qui se
  lisait comme une panne. La ligne d'étape, qui n'affichait alors que deux
  flèches éteintes, dit maintenant « Chargement de la page… ».
- Les chemins prennent une route en perspective ; l'épingle du lieu tenait la
  place, et un chemin n'est pas un lieu.
- Les quatre entrées de la racine ont chacune leur dessin : un parchemin, une
  tour crénelée, un crâne, une empreinte. La clef qui tenait la place des
  donjons a été rendue à son seul emploi, la colonne de droite, où elle dit
  qu'un donjon en exige une ; une recherche la montrait jusque-là deux fois sur
  la même ligne. Les trois lieux de combat gardent une teinte commune : la
  couleur dit le genre, la forme dit lequel des trois.
- Le champ de recherche dit ce qu'il accepte tant qu'on n'y a rien écrit : une
  zone, un succès, une quête ou un donjon. La croix qui le vide est passée
  dedans, où l'on voit ce qu'elle vide.
- Un donjon se suit comme une quête : un lien qui y mène reste dans la fenêtre,
  la liste rouvre sur sa branche, et il est retrouvé au lancement suivant.
- Une ancre de la page, comme « Aller directement à la mécanique du donjon »,
  ne passe plus pour une navigation étrangère et n'ouvre plus de seconde
  fenêtre.
- L'indicateur d'attente ne peut plus rester en l'air : il retombe si la vue
  échoue, et de toute façon au bout de vingt secondes, le journal disant alors
  qu'on a attendu pour rien. Le chemin de chargement est tracé de bout en
  bout.

- Les zones de quêtes suivent l'ordre de progression du jeu, sous leur nom court,
  avec un bloc « Quêtes supplémentaires » pour ce qui n'en relève pas.
- La recherche trouve aussi les zones et les succès, et les rend séparés ; les
  quêtes d'un succès trouvé sont listées sous lui.
- Un lien cliqué dans un guide ouvre une fenêtre à part, redimensionnable et
  toujours au-dessus, nettoyée du décor du site comme la fenêtre de quêtes.
- Le bandeau d'étape affiche un raccourci d'une phrase au lieu du paragraphe.
- Une carte devant les zones, une étoile devant les succès : la nature d'une
  ligne se voit sans la lire.
- Une croix vide la recherche, une autre ferme le panneau.
- Les fenêtres de l'application retrouvent leur place au lancement, y compris
  sur un second écran d'une autre densité.
- Le suivi de quêtes rouvre comme on l'a laissé, sur la dernière quête lue.
- Une icône annonce les prérequis d'une quête, et le survol les donne un par
  ligne, sous un titre et à la puce. Un clic épingle le panneau, et ceux qui
  sont des quêtes s'ouvrent d'un clic.
- Les quêtes se suivent au-delà de leur succès, par leurs prérequis : cent
  soixante-huit gagnent une suivante et cent quatre-vingt-dix-sept une
  précédente. Le bouton nomme la série d'arrivée quand on en change.
- Un indicateur d'attente prend la place du guide le temps qu'une page arrive.
- Une flèche revient sur la quête d'où l'on vient, quel que soit le chemin pris
  pour y arriver.
  Il manquait sur les liens de quête cliqués dans le guide : la navigation
  refusée puis relancée signalait sa fin après le départ de la vraie, et
  l'ancien guide restait à l'écran sans que rien ne l'annonce.
- Chaque nature de ligne porte son icône et sa couleur : un parchemin pour les
  quêtes, une clef pour les donjons, une épingle pour un lieu, un marque-page
  pour une famille de quêtes, une étoile pour un succès, un cadenas pour des
  prérequis. Les deux entrées de la racine sont dessinées pleines, les autres au
  filet.

### Modifié

- Le retour est un bouton fixe sous le fil d'Ariane, avec sa flèche et son mot :
  il défilait avec la liste et disparaissait dès qu'on descendait.
- Le panneau de recherche prend toute la hauteur : le bandeau d'étape et le pied
  de succès s'effacent tant qu'il est ouvert.
- La barre de recherche prend toute la largeur ; un indicateur discret remplace
  le compte de quêtes pendant l'indexation.
- Une plage de niveaux n'est affichée que lorsqu'elle repose sur assez de
  quêtes : le site ne renseigne le niveau que sur 117 des 782.
- Les encarts du site et les apartés entre parenthèses ne comptent plus pour des
  étapes.
- Le départ d'une quête est une étape à part entière, la première. Il était
  plaqué sur le premier paragraphe du guide, qui n'a le plus souvent rien à
  voir : sur seize guides relevés, treize ouvrent sur un préambule.
- Le départ s'affiche enfin : le premier paragraphe passait la marque de lecture
  avant qu'on ait rien fait défiler, et l'étape 1 n'existait qu'en théorie.
- Une étape choisie au bouton n'est plus reprise par le défilement qu'on vient
  de demander, ni perdue sur un guide trop court pour défiler.
- Le résumé d'étape ne finit plus au milieu d'un mot : le nom capturé débordait
  sur la suite de la phrase seize fois sur dix-huit, coupé net au quarantième
  caractère. Il reconnaît en plus « reparlez », « en parlant », « vos adieux à »
  et « présentez-vous à », relevés sur le site.
- Le personnage de départ vient des métadonnées et n'était pas relu : deux
  quêtes sur six cent quatre-vingt-treize y logent une phrase, d'où « Parlez à
  bateau pour vous rendre au village d'Albuera ».
- Un paragraphe en gras ne suffit plus à faire une étape : il lui faut des
  coordonnées ou un ordre donné au lecteur. Mesuré sur cinquante-cinq guides,
  cinq cent trente-deux paragraphes en gras ne donnaient que trois cent
  cinquante-sept consignes ; le reste décrivait des sorts de boss, commentait un
  choix de dialogue ou titrait une liste, et se retrouvait résumé faute de mieux
  par sa première phrase.
- La suite d'une série se cherche dans tout le succès et non dans sa seule
  dernière quête : sept succès y gagnent une continuation, dont « Médiation
  expéditive », qui se prolonge depuis sa cinquième quête sur six.
- Rouvrir la liste montre la rubrique de la quête affichée, celle-ci
  surlignée. Elle rouvrait sur la racine ou sur une recherche.
- Une recherche sans résultat dit « Aucun résultat ».
- Le survol des prérequis dit « Prérequis » et non « À faire avant » : sur cinq
  cent soixante-sept prérequis distincts, on trouve des objets à apporter, un
  alignement, un nombre de joueurs, un niveau et des créneaux horaires.
- Dans une recherche, les titres « Zones », « Succès » et « Quêtes » dominent
  les succès qu'ils coiffent, au lieu de leur ressembler.
- « Ouvrir dans le navigateur » ouvre ce que la fenêtre montre : l'accueil du
  site à la racine du menu, la page des zones quand on les parcourt, celle d'une
  rubrique quand on y est entré, la quête sinon. Elle menait toujours à la quête, y compris quand la liste
  couvrait l'écran.
- Un blanc sépare « Quêtes principales » des lieux, qu'elle ouvrait sans en
  être un.
- Le crédit « Guides de papycha.fr » porte la couleur d'accent.
- La recherche ne retient plus une quête pour un mot qui n'existe que dans le
  nom de sa zone : « frigost » en rendait cent soixante-dix-sept, dont cent
  soixante-treize par ce seul chemin, et « bworks » onze sans qu'aucune ne porte
  le mot. C'est au groupe des zones que revient cette recherche-là.
- Les prérequis remplacent le niveau à droite d'une quête : le site les donne
  pour six cent treize quêtes contre cent dix-sept pour le niveau.
- Un lien vers une quête du catalogue est suivi dans la fenêtre plutôt que dans
  une seconde, comme le bouton « précédente » qui mène au même endroit.
- Le bloc « Quêtes précédentes » du site disparaît des guides : le pied de la
  fenêtre y mène déjà. Le bandeau d'intro entier part avec lui quand il ne lui
  reste rien à dire, ce qui est le cas hors des quêtes répétables.

### Corrigé

- Suivre la quête précédente ou suivante éteignait la navigation : le pied se
  vidait après un seul saut et il fallait repasser par la liste.
- Rouvrir le panneau sélectionne la quête ouverte, sans reconstruire la liste.
- La reconnaissance des fenêtres de l'application interrogeait la fenêtre de
  quêtes depuis le guet du premier plan, qui ne vit pas sur le fil de
  l'interface : chaque changement de fenêtre levait une exception et les
  raccourcis restaient dans leur état précédent.
- Les sept rubriques venues d'une page du site n'étaient cherchables par aucun
  chemin, et taper « quête » ramenait le catalogue entier.
- Le bouton de retour en haut du site et sa bande verte réapparaissaient au
  premier défilement : ils étaient masqués par style en ligne, que le fondu du
  greffon écrasait.
- Le haut d'une page dépassait dès que la fenêtre passait cinq cent quarante
  pixels de large, seuil sous lequel le site annule lui-même une marge de
  cinquante pixels.
- Le relevé des succès retirait des intitulés de prérequis un préfixe que le
  site n'emploie plus : douze liens de l'ordre de jeu se perdaient en silence.
- La lecture du fichier de succès livré était sensible à la casse et n'y voyait
  aucune entrée, sans le dire.

- Suivi de quêtes adossé à papycha.fr : fenêtre toujours au-dessus ouverte par
  `Ctrl+Q`, recherche par rubrique et par succès sur un catalogue de 782 quêtes,
  sélecteur d'étape et navigation dans la chaîne du succès.
- Contrôle de la version d'Android avant le lancement : un appareil sous
  Android 10 ou plus ancien est écarté aussitôt, en disant sa version, au lieu
  d'échouer au bout de trente secondes sur un message deviné.
- Fiches d'aide pour vivo et iQOO, et pour Amazon Fire, avec l'avertissement que
  Fire OS n'a pas le Play Store.
- Manifeste applicatif déclarant la conscience de la mise à l'échelle écran par
  écran et le socle Windows 10 1809.

### Modifié

- Le repli de définition descend les paliers au lieu de s'arrêter à 1080, ce qui
  couvre enfin les encodeurs plafonnés à 1280x720, et conserve le rapport
  d'image de l'écran au lieu d'imposer du 16:9.
- Les refus de scrcpy sont rangés en catégories : seuls ceux qu'une définition
  plus modeste peut réparer sont retentés. Un téléphone débranché ne coûte plus
  une seconde attente.
- La détection du jeu retient les copies installées sous un nom de paquet
  dérivé, que la comparaison stricte rendait invisibles.
- Le repli de la liste des profils Android est signalé dans le bandeau au lieu
  de passer pour un appareil qui n'a qu'un profil.
- Les chemins de menu de l'aide valent aussi pour une tablette, et les trois
  fenêtres d'aide disent qu'ils supposent un appareil réglé en français.
- Le repli d'Android sans surcouche nomme les constructeurs qu'il couvre :
  ASUS, TCL, ZTE, HMD, Fairphone, Transsion.

### Corrigé

- La densité d'affichage était bornée à 800 au calcul puis rabotée à 640 juste
  avant scrcpy : tout ce qui se trouvait entre les deux disparaissait en
  silence, et le zoom le plus proche saturait avant la hauteur annoncée.
- Un codec vidéo inconnu de scrcpy est écarté au lieu de lui être transmis.
- Depuis la recherche de quêtes, la flèche du bas s'arrêtait sur un intertitre
  de rubrique et Entrée ne faisait rien.

- Squelette de la solution .NET 10 : `DtHub.Core`, `DtHub.Infrastructure`,
  `DtHub.App` (WPF) et `DtHub.Tests` (xUnit).
- Identité produit centralisée pour permettre un renommage simple.
- Icône applicative générée par script reproductible.
