# Changelog

Toutes les modifications notables de ce projet sont consignées ici.

Le format suit [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/)
et le projet respecte le [versionnage sémantique](https://semver.org/lang/fr/).

## [Non publié]

### Modifié

- Les écrans dessinés dans les aides ressemblent enfin à ce qu'ils montrent. Ils
  portent une barre d'état, une flèche de retour et un grand titre comme les
  réglages d'Android depuis leur douzième version, et leurs lignes ont chacune
  son icône et son chevron dans une carte arrondie. C'étaient auparavant des
  barres grises sous un mince bandeau de titre, qui n'évoquaient aucun téléphone
  en particulier.

  Chaque ligne porte une pastille de couleur, comme Android range ses réglages,
  et une ligne sur trois annonce son état sous son nom. Un écran porte un
  interrupteur, un seul : un écran par lequel on ne fait que passer n'en porte
  pas quatre.

  La longueur des barres est inégale, sans quoi cinq intitulés de même taille
  trahissaient le dessin, mais rien ne bouge d'une ouverture à l'autre : le même
  écran se rend toujours pareil, teintes et interrupteur compris. Cinq lignes par
  écran au lieu de quatre.

  Ce qui n'est pas dit reste muet : ni les autres intitulés du menu, ni l'heure,
  ni le niveau de batterie ne sont inventés. Les pastilles ne disent pas la
  couleur d'un réglage en particulier, que la fiche de marque ne donne pas, mais
  qu'il y en a une.

- Fermer le cadre à onglets ferme les comptes qu'il logeait. Ils en ressortaient
  libres, et l'on se retrouvait avec autant de fenêtres de jeu éparses qu'on
  croyait venir de fermer : le geste ne faisait pas ce qu'il annonce. Chaque
  compte se comporte comme si l'on avait fermé sa fenêtre, sa place est retenue
  et il ne rouvrira pas de lui-même au prochain démarrage. Il reste logé en
  onglets, et y retournera le jour où on le rouvre.

  Les fenêtres quittent le cadre avant de se fermer, et masquées : une fenêtre
  logée est fille du cadre, et Windows détruirait ses enfants avec lui sans
  laisser à scrcpy le temps de s'arrêter proprement.

- Une quête seule peut désormais se glisser entre deux quêtes d'un succès, et
  la série reprend après elle sous un intertitre marqué « suite ». Au Château
  d'Amakna, « Étre plus royaliste que le roi » réclame neuf quêtes seules au
  milieu de sa propre suite : traité comme un bloc insécable, il les rejetait
  toutes avant ou toutes après, et la liste montrait des quêtes avant ce
  qu'elles exigent.

  Deux succès, eux, ne s'entrelacent jamais. Les laisser faire lèverait les
  onze fautes restantes, mais l'île de Frigost, où huit succès se réclament
  mutuellement, devenait un va-et-vient de quinze intertitres entre les mêmes
  séries.

  Mesuré : les prérequis placés après la quête qui les réclame passent de onze
  à sept, et quatre succès sur cent soixante et un sont coupés.

- La barre du bas se lit en deux temps. « Guides » est seul à gauche, dans son
  propre cadre : il ne range rien, contrairement aux deux autres, et les mettre
  tous les trois dans le même cadre les donnait pour trois gestes de même
  nature. Empiler et côte à côte gardent leur cadre commun et perdent leur
  texte : leur dessin dit ce qu'elles font, et le pied de fenêtre respire.

- Empiler et côte à côte ne paraissent plus qu'avec de quoi ranger : deux
  fenêtres au moins, et que ces placements peuvent bouger. Une seule fenêtre
  ouverte, ou deux logées dans le cadre à onglets, et les presser ne faisait
  rien. Elles se retirent maintenant plutôt que de ne rien faire, et
  reparaissent dès qu'une deuxième fenêtre libre s'ouvre.

### Corrigé

- La liste d'une zone montrait des quêtes avant ce qu'elles exigent. Dans
  « Un Piou, c'est tout ! », « L'île Céleste » paraissait avant « Le voyage vers
  Incarnam », qu'elle réclame : la carte des rangs, calculée à l'indexation, se
  contredisait elle-même sur treize quêtes. Les quêtes d'un succès se rangent
  maintenant par leurs prérequis d'abord, la carte ne servant qu'à départager
  celles qu'aucun prérequis ne sépare.

  Une quête qui réclame son propre succès le réclame en entier : elle passe donc
  après tout le reste de son bloc. « En route pour Plantala » est seule dans ce
  cas.

- Une quête seule qui découle d'un succès était rangée après **tous** les
  succès, et non derrière le sien. « La découverte d'un vaste monde », dont le
  seul prérequis est le succès « Devenir une légende », se retrouvait trente
  rangs plus bas à Astrub, entre des quêtes qui n'ont rien à voir. Elle prend
  désormais le rang du succès dont elle découle, et le suit immédiatement ; une
  suite de quêtes seules le suit tout entière, dans l'ordre où on l'enchaîne.

  Mesuré sur les vingt-cinq listes : les prérequis placés après la quête qui les
  réclame passent de treize à onze. Les onze qui restent viennent de boucles,
  deux blocs se réclamant l'un l'autre, qu'un succès insécable ne peut pas
  départager.

- La quête suivante manquait à la fin d'une série. « La légende du Chevalier de
  l'Automne » clôt le succès « Un nouveau départ » et le site propose en pied
  d'article « Dans les pas du Chevalier de l'Automne », première quête du succès
  suivant ; le bouton, lui, disparaissait.

  Deux causes. Le site écrit ses prérequis de trois formes, et l'application
  n'en lisait qu'une : soixante-treize libellés sur cinq cent quatre-vingt-quatre
  nommaient un jalon ou un succès entier plutôt qu'une quête, et l'arête ne
  reliait rien. Et le bloc de progression que le site publie en pied d'article,
  celui qui donne la quête précédente et la suivante, était analysé puis jeté.

  Le site fait foi désormais : quand il nomme une voisine, et une seule, c'est
  elle. Mesuré sur les sept cent quatre-vingt-deux guides : huit succès de moins
  s'achèvent sur un cul-de-sac, quatre suivantes et deux précédentes
  apparaissent, quarante-huit suivantes et quatre-vingt-dix-neuf précédentes
  suivent maintenant l'ordre du site plutôt que le nôtre. Des quatre-vingt-un
  succès qui restent sans suite, soixante-dix-sept sont muets sur le site lui-même.

- Le bloc de progression du site était masqué dans la fenêtre des pages liées,
  qui n'a pas de pied de fenêtre pour le remplacer : l'information y était perdue
  sans contrepartie. Il n'est plus masqué que dans la fenêtre des guides.

### Ajouté

- L'application se montre en anglais, en français ou en espagnol. Elle suit la
  langue d'affichage de Windows sans qu'on ait rien à régler, et retombe sur
  l'anglais quand cette langue n'est pas traduite. Un choix dans l'onglet
  Fenêtres permet de la contredire ; il prend effet au démarrage suivant, les
  fenêtres lisant leurs textes une fois pour toutes à leur construction.

  Toutes les fenêtres sont traduites, infobulles comprises, ainsi que les
  messages d'erreur, les noms de touches et les phrases bâties à l'exécution.

  Ce qui reste français y reste à dessein : les guides viennent de papycha.fr
  et sont du contenu, non de l'habillage ; les noms de rubriques et de quêtes
  servent de clés d'appariement avec le site ; les chemins de menus Android
  sont ceux du téléphone, et l'aide le dit déjà en toutes lettres.

### Corrigé

- L'ordre des onglets rangé à la souris ne survivait pas au démarrage suivant
  quand un profil s'ouvrait tout seul : le profil rejouait l'ordre qu'il avait
  retenu. L'ordre des comptes est un réglage général, celui de la liste comme
  celui des onglets ; un profil le retient pour être lisible, il ne l'impose
  plus. Il tient donc pour la session, pour les profils et d'un lancement à
  l'autre.

  Les onglets sont par ailleurs remis dans cet ordre à chaque arrivée, et non
  laissés dans celui des arrivées : les afficheurs ne se préparent pas à la
  même vitesse.

- Suivre un lien du guide vers un chemin, un donjon, un raid ou une tanière
  perdait le fil : la flèche de retour ne paraissait pas, et rouvrir le panneau
  montrait la branche de ce qu'on venait d'ouvrir au lieu de la fiche d'où l'on
  venait. L'historique n'était empilé que dans la branche des quêtes, et il ne
  savait retenir que des quêtes.

  La règle est maintenant la même pour toutes les natures : un lien s'ouvre à
  part quand le catalogue ne le connaît pas, sur place quand il le connaît, et
  dès qu'il s'ouvre sur place la flèche paraît et le panneau garde la fiche
  d'origine. Choisir dans la liste efface la piste : on a désigné où aller.

- Le repli des boutons « précédente » et « suivante », quand la page n'est pas
  au catalogue, laissait l'état sur la page d'avant : le panneau rouvrait sur
  l'ancienne rubrique et le signalement nommait l'ancienne quête.

- Une adresse du catalogue ornée d'une ancre, « …/quete-x/#etape-3 », partait en
  fenêtre annexe alors qu'elle devait s'ouvrir sur place.

- Le formulaire de signalement se soulignait dès qu'on cliquait dans un champ.
  Le thème du site porte `label:focus { text-decoration: underline }`, et ses
  champs sont écrits `<label><span>intitulé</span><input></label>` : la
  décoration se propageait donc à l'intitulé et à la valeur. Elle est coupée là
  où elle naît, sur le libellé, une décoration ne s'annulant pas depuis ses
  descendants.

- Le bloc « Remonter une erreur » ne se repliait plus. Son résumé était masqué,
  en croyant qu'il faisait doublon avec le titre de la fenêtre ; c'était le seul
  moyen d'ouvrir et de refermer le bloc. Il est de retour, ramené à gauche.

- Les trois bulles de l'application demandaient deux clics pour rouvrir après
  s'être refermées d'elles-mêmes : la liste des étapes, celle des profils et
  celle des réglages fins. Leur ouverture ne suivait la bascule que dans un
  sens, si bien qu'une bulle refermée au clic ailleurs laissait la bascule
  cochée.

- La liste rouvrait sur la mauvaise rubrique. Une quête sur cinq appartient à
  plusieurs rubriques, et le catalogue n'en retient qu'une, la moins peuplée :
  rouvrir la liste depuis une quête de Frigost basculait sur « Quêtes
  principales ». Relevé sur le catalogue, 215 quêtes sur 782 sont dans ce cas,
  dont 93 qu'une rubrique transverse emporte et 80 dans l'autre sens. La liste
  garde désormais la rubrique d'où l'on vient quand la quête y figure, et
  l'étiquette de série des liens précédente et suivante suit la même règle.

- L'encart « Type : Principale » tenait lieu de première étape dans la fenêtre
  des guides. Il n'était pas compté comme étape, mais l'étape de départ est
  ancrée en haut de l'article, et cet encart était le seul morceau du bandeau
  d'intro que la fenêtre ne masquait pas : c'est donc lui qu'on voyait. Le
  bandeau y passe maintenant en entier, ses quatre blocs étant repris par le
  nôtre.

- Trois familles de paragraphes étaient comptées comme étapes sans en être.
  Relevé sur les 782 guides du site, quarante et une étapes sur 3 293 :

  - **L'annonce du départ écrite en prose**, « La quête se lance en [2,-16] en
    parlant à Kerubim Crépin », que le bandeau donne déjà en première étape.
    Trente cas. Elle est gardée quand elle porte en plus sa propre consigne.
  - **Le récit au présent**, « vous vous faites agresser par x2 Bandit ». Le
    garde qui distingue « vous partez » de « partez » ne valait pas pour les
    six verbes irréguliers. Sept cas.
  - **Les encarts « Important : »**, de même nature que « Attention : » et
    « Prérequis : » que la règle écartait déjà. Quatre cas.

  Chacune des quarante et une a été relue. Aucun guide ne perd sa dernière
  étape, et aucune étape n'apparaît là où il n'y en avait pas.

- Le bouton « Ajouter un compte » créait un compte inutilisable. Il faisait un
  utilisateur Android complet, et un utilisateur complet ne peut pas porter de
  fenêtre pendant qu'un autre compte est au premier plan : mesuré sur un Xiaomi
  23078PND5G sous Android 16, `cmd user is-user-visible` rend faux, et
  `am start` répond pourtant `Status: ok` avant de pendre soixante-dix secondes
  sans rien afficher. Le bouton crée désormais un profil rattaché au compte
  principal, dont la fenêtre s'ouvre en quatre secondes, vérifié jusqu'à
  l'écran de connexion du jeu.

- Un refus du téléphone était annoncé comme « l'application n'est plus
  installée », ce qui envoyait réinstaller un jeu bien présent. C'est ce que
  rendent le Dossier sécurisé de Samsung et les profils tenus par une politique
  d'entreprise. Un échec non reconnu reste maintenant sans interprétation, et un
  refus de permission est nommé pour ce qu'il est.

- Un profil en pause n'était pas vu. C'est l'interrupteur du profil
  professionnel, et la fonction principale de Shelter et d'Island : le lancement
  échouait sans que rien n'explique pourquoi. Le drapeau est désormais lu, le
  profil écarté avant le lancement, et le message dit de le rallumer sur le
  téléphone. Aucune commande ADB ne permet de le faire à sa place.

- Le démarrage d'un profil ignorait son propre résultat et lançait quand même.
  L'échec se manifestait plus loin, sous une forme que personne ne rattachait au
  profil.

### Ajouté

- Un bouton **Signaler une erreur**, dans le pied de la fenêtre des guides. Il
  ouvre le formulaire de signalement du site sur la page qu'on lit, dans une
  fenêtre à part, avec le champ « Où se trouve l'erreur ? » déjà rempli : la
  zone et la quête, c'est-à-dire ce que le site nomme lui-même.

  Rien n'est envoyé et rien d'autre n'est écrit : la description reste vide,
  c'est ce que le lecteur a vu, et c'est lui qui appuie. Le repère peut être
  effacé, et il n'écrase jamais une saisie en cours.

  La fenêtre ne montre que le formulaire : le reste de l'article y est masqué,
  le fond photographique du site remplacé par le nôtre, et les champs ramenés à
  la largeur disponible. La zone de texte ne s'étire plus à la poignée, sa
  hauteur suit celle de la fenêtre, et le formulaire défile.

  Le bouton d'envoi du site est rendu visible : sa feuille de style lui donne
  « background: currentColor » avec « color: Canvas », si bien que son fond
  prend la couleur de son propre texte et qu'il disparaît.

  Seuls les articles portent ce formulaire, quêtes, donjons et chemins compris ;
  les rubriques n'en ont pas et le site n'en a pas de général. Depuis une
  rubrique, le bouton mène donc à la page de contact du site, qui renvoie vers
  le serveur Discord de l'équipe.

- **Choisir une étape dans une liste**, en dépliant son rang dans le bandeau des
  guides. Les deux flèches n'avancent que d'une étape à la fois : revenir à la
  troisième d'un guide qui en compte treize demandait neuf clics, et rien ne
  disait ce qu'on trouverait en chemin. Chaque entrée porte son numéro et ce
  qu'il y a à y faire, celle où l'on est se distingue, et la page s'y replace.

- Un mode onglets : une icône sur la ligne du compte le loge dans un cadre
  unique, comme un onglet de navigateur, et un second clic lui rend sa fenêtre
  libre. Autant de comptes qu'on veut, un seul visible à la fois, et passer de
  l'un à l'autre est immédiat : les fenêtres sont cachées, non fermées.

  Les onglets se glissent pour changer leur ordre, qui est aussi celui de la
  liste des comptes : un seul ordre partout. Un trait bleu montre où l'onglet se
  posera, à gauche ou à droite de celui qu'on survole, et lâcher après le dernier
  range en fin de liste. L'onglet part à l'instant où on lâche, sans attendre le
  trajet par les réglages.

  Le cadre s'attrape par n'importe quel bord, comme une fenêtre de jeu libre :
  tirer un côté commande la hauteur, tirer le haut ou le bas commande la
  largeur, et le bord opposé ne bouge pas.

  Le cadre prend la forme de l'image du jeu et la garde à chaque
  redimensionnement, si bien qu'aucune bande noire ne subsiste sur les côtés :
  scrcpy verrouille le rapport de ce qu'il rend, et une zone d'accueil d'une
  autre forme lui laissait forcément du noir. S'il n'y a plus de place en
  hauteur, c'est la largeur qui cède.

  La fenêtre n'est ni recréée ni rouverte, seulement logée : basculer ne coûte
  ni les secondes d'une ouverture ni un nouvel afficheur virtuel sur le
  téléphone. Un compte logé échappe aux placements automatiques, qui
  lutteraient contre le cadre. Le cadre se referme quand son dernier onglet le
  quitte, et un onglet part avec la session qu'il montre : le laisser faisait
  croire que le compte était encore logé, et le rouvrir le sortait du cadre.

- Des profils de lancement : un ensemble de comptes **avec leurs positions et
  leurs réglages**, qu'on retient sous un nom et qu'on rouvre d'un geste.
  « Solo donjon » ouvre un compte en grand et en qualité haute, « Duo pêche »
  deux fenêtres côte à côte en qualité moyenne. Un profil emporte la position et
  la taille de chaque fenêtre, la qualité et sa personnalisation, la distance
  dans le jeu, l'ancrage et la taille en pourcentage, le son du jeu renvoyé sur
  le PC, le presse-papiers partagé, quels comptes sont en onglets et où était le
  cadre qui les logeait.

  Un profil peut être désigné pour le démarrage ; sans désignation, l'application
  rouvre ce qui était ouvert, comme avant. L'ouvrir ferme les fenêtres qui n'en
  font pas partie et ouvre celles qui manquent, après confirmation.

  Ils vivent derrière un bouton « Profils », sur la ligne du bouton
  d'association : dépliés dans la page, ils prenaient quarante-sept pixels à la
  liste des comptes, qui en manque dans une fenêtre courte. La bulle les montre
  tous, une ligne chacun, avec à droite de quoi l'ouvrir, le désigner pour le
  démarrage et le supprimer, cette dernière après confirmation. Désigner une
  ligne ne fait plus rien par soi-même : ouvrir est un geste à part, faute de
  quoi un simple clic d'exploration fermait des fenêtres de jeu en cours.

  « Créer un profil » annonce ce qu'il va retenir avant de demander un nom, avec
  les valeurs du moment plutôt qu'une liste figée : le nombre de comptes
  ouverts, la qualité, la distance dans le jeu et lesquels sont en onglets.

  Un compte retiré du téléphone depuis l'enregistrement est simplement ignoré :
  le profil garde sa raison d'être et les autres comptes s'ouvrent. Un profil
  enregistré avant que les profils ne portent les positions ouvre encore ses
  comptes, là où ils étaient.

- Un quatrième palier de qualité, « Personnalisé ». Ses réglages fins,
  définition maximale, cadence, finesse d'image et codec vidéo, s'ouvrent dans
  une bulle par le rouage qui paraît à côté de lui : dépliés dans la carte, ils
  lui faisaient gagner deux cents pixels de haut et repoussaient tout le reste
  du panneau. La hauteur ne bouge donc plus selon le palier choisi.

  La finesse se règle en bits par pixel, et non en mégabits comme le proposent
  les interfaces qui ne pilotent qu'un seul miroir. Ici la définition de
  l'afficheur suit la taille de la fenêtre : un débit absolu servirait
  grassement une petite fenêtre et affamerait une grande, ce que le reste du
  code avait précisément appris à ne plus faire. La définition, elle, est
  annoncée pour ce qu'elle est, un plafond.

- Le palier « Maximale » s'appelle désormais « Haute », et son rouage de
  réglages fins se tient sur la ligne du titre plutôt que sous les paliers, où
  il retombait seul à la ligne.

- Le nom du jeu paraît là où il lève une ambiguïté, et pas ailleurs : dans
  l'en-tête, sur les fenêtres « de jeu », et au-dessus de la liste des comptes.
  Il ne paraît pas dans la section « Sur le téléphone » : le son capté est celui
  de l'appareil entier et non celui du jeu, l'écran est celui du téléphone, les
  animations sont celles d'Android. L'y nommer aurait été faux.

- Une troisième ligne dit la définition réellement demandée, et prévient quand
  le plafond choisi n'y change rien : « vos fenêtres tournent en 2560 × 1440,
  au-delà ce réglage ne change rien ». Le palier retenu est le premier au-dessus
  de la fenêtre, donc monter le plafond plus haut que les fenêtres ne demande
  rien de plus, alors que l'interface laissait croire l'inverse.

- Sous ces quatre réglages, deux lignes qui disent ce qu'ils valent :
  « 0,090 bit par pixel et par image, confortable » puis « au plus 11,2 Mb/s
  par fenêtre, 22,4 Mb/s à 2 comptes ». La première est la mesure que l'encodeur
  reçoit vraiment, et celle dont l'absence avait laissé passer un débit à
  l'envers dans les paliers automatiques ; le codec y entre, H.265 rendant
  davantage à débit égal. La seconde compte les fenêtres ouvertes sur le
  téléphone, parce qu'elles partagent une seule liaison et un seul encodeur.

- Deux codecs au choix, H.264 et H.265. AV1 et VP8 ne sont pas proposés : relevé
  par `scrcpy --list-encoders`, un téléphone ordinaire n'a pour eux qu'un
  encodeur logiciel, qui coûterait bien plus qu'il ne rend.

- Le son du téléphone se renvoie sur le PC. C'est celui de l'appareil entier,
  Android ne sachant pas l'isoler par application : une seule fenêtre par
  téléphone le porte, sinon le même flux reviendrait en plusieurs exemplaires.

- Chaque compte porte l'icône du jeu, celle qui est sur le téléphone. Elle est
  tirée d'une seule entrée de l'archive de l'application, cinquante et un
  kilooctets pour une archive de quatorze mégaoctets qui ne bouge pas, puis
  gardée dans le cache. Une application dont l'icône n'est pas extractible
  n'affiche rien, et la liste reste ce qu'elle était.
- Un bouton ajoute un compte sur le téléphone, à droite de son nom. Il crée un
  profil Android, y installe le jeu et le démarre : le compte apparaît dans la
  liste, prêt à ouvrir, sans redémarrer l'application. C'est le mécanisme des
  comptes multiples d'Android, celui que la surcouche du téléphone emploie
  elle-même : rien n'est recopié, l'application reste celle de l'éditeur,
  signée par lui. Le profil naît vide, et le jeu y redemandera ses ressources
  et la connexion, ce que la confirmation annonce.
- L'aide « Plusieurs comptes sur un même appareil ? » disparaît : le bouton fait
  ce qu'elle expliquait. Ses fiches de marque servent encore, mais seulement
  quand le téléphone refuse la création, pour dire où aller à la main.
- Un compte dont le profil a été supprimé sur le téléphone quitte la liste tout
  seul. Il y restait indéfiniment, sans qu'aucun bouton puisse l'en retirer.
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
- Les quêtes qu'aucun succès ne réclame ne sont plus rejetées en fin de liste.
  Elles se rangent à leur place dans la progression, suivant leurs prérequis :
  « Une arrivée mouvementée » ouvre désormais Albuera devant « Médiation
  expéditive », « En route pour Feudala » se glisse entre « Sous le bois de sa
  colère » et « Sous des nuages de cendre ». L'intertitre « Hors succès »
  disparaît.
- Les quêtes d'un succès se décalent et se relient par un filet vertical. C'est
  ce qui dit maintenant l'appartenance : l'intertitre et ses quêtes étaient au
  même retrait, et une quête au ras de la marge se reconnaît sans rien avoir à
  apprendre.
- Une quête que rien ne lie, ni prérequis reconnu ni quête qui la réclame, reste
  en fin de liste au lieu de se glisser au hasard entre deux succès. Au Château
  d'Amakna, « On recherche Ali Grothor » se retrouvait ainsi entre « Le vallon du
  château » et « Étre plus royaliste que le roi », sans rapport avec ni l'un ni
  l'autre. Cent deux quêtes seules sur trois cent une sont dans ce cas.
- La sentinelle des guides interroge les cinq catégories qu'on lit, et non plus
  le site entier. Sa date de dernière modification bougeait dès qu'un seul de ses
  mille douze articles était touché, même un dont on ne lit rien : une virgule
  ailleurs coûtait cinquante secondes de relecture. Cinq demandes de quarante
  octets, deux cents en tout, et les catégories bougent chacune à leur rythme :
  les raids n'ont pas changé depuis le 28 août, les tanières depuis le 18.
- Le moteur de rendu s'endort quand les guides se masquent, et se réveille quand
  ils reviennent. Mesuré : quarante-quatre mégaoctets rendus sur quatre cent
  soixante-trois. Les six processus restent, seule leur mémoire de travail se
  relâche.
- L'arrêt de l'application laisse tourner sa boucle de messages pendant qu'il
  range. WPF coupe le répartiteur dès que la méthode d'arrêt rend la main, et un
  « await » la lui rend : tout ce qui suivait, dont la pose de la mise à jour,
  se serait perdu dès qu'une fenêtre de jeu aurait été ouverte. Mesuré à la
  sonde, la suite s'exécute aujourd'hui, mais seulement parce que fermer zéro
  fenêtre se termine d'un trait. L'attente est bornée à huit secondes.
- Le guide rouvre à l'étape où on l'avait laissé, et plus à la première. Elle
  n'est reprise que si la page en compte encore autant : le site peut l'avoir
  raccourcie depuis.
- Une chaîne de contrôle compile et éprouve à chaque poussée, avertissements
  traités en erreurs. La seule chaîne existante ne se déclenchait que sur une
  étiquette de version : entre deux livraisons, un test rouge ne se voyait que
  sur la machine de celui qui avait écrit le code.
- La carte d'un donjon ne recouvre plus l'en-tête de la page. Elle remonte de
  quatre-vingt-quatorze pixels pour se glisser à côté, et recouvrait le niveau et
  la pierre d'âme dès que la fenêtre passait huit cent quatre-vingts pixels de
  contenu. Ce n'est pas notre mise en page : le site n'atteint jamais cette
  branche, sa colonne de guide faisant six cent cinquante pixels, un volet
  latéral prenant le reste. Nous écartons ce volet, donc nous y tombons.
- L'exécutable porte enfin son nom. Ses propriétés annonçaient « DT Touch »,
  resté d'un renommage, quand la fenêtre dit « DT Hub », et sa description était
  le nom du fichier. C'est ce que Windows montre et ce que SmartScreen cite. La
  chaîne de livraison refuse désormais un binaire dont le nom, la description ou
  l'éditeur manquent, et sait le signer dès qu'un certificat est configuré.
- L'application n'écrit plus rien à côté de son exécutable. Le moteur de rendu y
  posait son cache, faute qu'on lui dise où aller : vingt-quatre mégaoctets après
  une seule session, trois cent quatre-vingt-dix-neuf après quelques semaines. Il
  écrit maintenant sous `%LOCALAPPDATA%\DtHub\webview`, avec un cache borné à
  cent mégaoctets et balayé s'il déborde.
- La publication ne rend plus qu'un fichier. Trois fichiers de symboles et trois
  de documentation d'un paquet tiers traînaient à côté ; les symboles sont
  désormais embarqués, ce qui garde les numéros de ligne dans les journaux.
- L'application pose un raccourci dans le menu Démarrer, sur l'exécutable là où
  il se trouve. Elle ne se copie ni ne se déplace ; déplacer le fichier corrige
  le raccourci au démarrage suivant. Rien n'est fait depuis un arbre de sources.
- Le composant WebView2 absent se dit en clair, avec le lien pour l'installer, au
  lieu d'une fenêtre vide et d'une ligne de journal. C'est la seule dépendance
  externe de l'application.
- Les guides suivent le site au lieu de suivre un calendrier. L'application lui
  demande s'il a bougé, ce qui coûte quatre-vingt-dix-sept octets, et ne le relit
  que s'il a bougé. Une quête parue le matin était vue jusqu'à sept jours plus
  tard ; elle l'est le jour même, et pour dix-huit mégaoctets de moins quand rien
  ne change.
- Une relecture dit ce qu'elle a rapporté, « 3 quêtes de plus », sans qu'on
  l'ait demandée : personne ne demande une relecture, la sentinelle décide, et
  l'on veut savoir ce qu'elle a trouvé. Elle se tait quand le catalogue n'a rien
  gagné ni perdu, ce qui est le cas courant, le site remaniant souvent ses pages
  sans en ajouter.
- Une sonde de développement vérifie que le site se lit encore comme
  l'application le suppose : titres des donjons, sommaires des raids et des
  tanières, absence de sommaire sur les guides de quête, et une quinzaine de
  comptes comparés à un relevé de référence. Elle rend 1 en cas d'écart. Les
  deux défauts trouvés à l'œil aujourd'hui, tanières et raids sans étapes,
  auraient été dits par elle.
- Redémarrer le poste retient désormais la place des fenêtres. Elle n'était
  enregistrée qu'au « Quitter » : une fin de session Windows la perdait, et les
  fenêtres revenaient à leur place de l'avant-dernière fois. L'écriture est
  attendue avant l'arrêt, bornée à trois secondes des cinq que Windows accorde.
- L'application se met à jour depuis les livraisons du dépôt. Elle demande la
  dernière au démarrage, la télécharge en fond si la case « Se mettre à jour
  toute seule » est cochée, vérifie son empreinte, et pose le nouvel exécutable
  quand on quitte : jamais en pleine session. La note de version paraît au
  démarrage suivant, celui qui exécute enfin la nouvelle version, et un bandeau
  la rend consultable avant.
- Elle refuse de se mettre à jour depuis un arbre de sources : le lanceur de
  développement republie à chaque démarrage et écraserait la mise à jour dans la
  seconde, en faisant croire à une régression.
- Une chaîne de livraison publie l'exécutable, son empreinte et la note de
  version quand une étiquette « v… » est poussée. La marche à suivre est dans
  docs/LIVRAISON.md.
- Les guides tiennent l'application en vie à eux seuls. Fermer la dernière
  fenêtre de jeu, ou masquer les réglages, emportait le guide qu'on était en
  train de lire. Ils comptent désormais comme le panneau : tant qu'ils sont à
  l'écran, l'application continue ; les masquer alors qu'il ne reste rien
  d'autre l'arrête, comme masquer le panneau.
- Le panneau des prérequis s'ouvre à gauche du cadenas et non plus dessous, où il
  recouvrait les lignes suivantes et se calait sur un décalage fixe que la
  largeur du texte démentait. Un second clic sur le cadenas le referme.
- Ces quêtes s'enchaînent aussi entre elles au lieu de se ranger par titre. Les
  quatre-vingts quêtes d'alignement bontarien, qui se lisaient « bontarien 1,
  10, 11, 12, 2 », se lisent dans l'ordre.
- La chaîne de quêtes et la liste rangent enfin un succès de la même façon. La
  chaîne triait à l'endroit un rang de jeu inconnu, donc en tête, là où la liste
  le met en queue : une quête de rang inconnu passait pour la première de son
  succès et se donnait pour la suite de la série précédente.
- La fenêtre s'appelle « Guides » et non plus « Quêtes » : elle ouvre aussi des
  donjons, des raids, des tanières et des chemins. Le raccourci et l'infobulle
  du bouton disent de même.
- Le rond d'attente était dessiné à seize unités quel que soit sa taille : à
  vingt-deux pixels, l'arc bleu restait seize et se retrouvait décalé en haut à
  gauche d'un anneau plus grand que lui. Il est maintenant dessiné à taille fixe
  et mis à l'échelle. L'anneau et l'arc n'avaient d'ailleurs pas le même rayon,
  huit contre sept, ce qui les décalait d'un pixel entier même à seize.
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

- **Les touches de l'application se ressemblent enfin.** Ce qui se presse porte
  un angle de cinq pixels, ce qui porte du contenu en garde deux : la
  distinction se voit sans qu'on ait à la nommer, là où deux pixels sur une
  touche de trente de haut lisaient comme un rectangle inachevé. La face prend
  un dégradé à peine perceptible, plus clair en haut, et s'éclaircit sous le
  doigt au lieu de s'effacer : une baisse d'opacité rapprochait la touche du
  fond, ce qui est le contraire de ce qu'un survol doit dire.

- **La barre du bas est un groupe de touches nommées.** Trois icônes flottantes
  y surmontaient chacune son raccourci en chasse fixe : « Ctrl + » paraissait
  trois fois dans un pied de fenêtre qui ne demande qu'à se faire oublier, et il
  fallait survoler pour savoir laquelle faisait quoi. Un seul cadre, deux
  filets, et « Empiler », « Côte à côte », « Guides » écrits à côté de leur
  dessin. Les raccourcis se lisent dans l'onglet qui leur est consacré, et
  l'infobulle dit ce que chaque touche fait.

  Les trois dessins sont redevenus des tracés, comme partout ailleurs dans
  l'application : ils étaient bâtis en rectangles imbriqués, et celui du
  côte-à-côte se remplissait de son propre trait à cette taille.

- Le plus du bouton d'association perd sa pastille et redevient un tracé, du
  même trait que le signet de la touche voisine : la pastille lui donnait un
  poids que l'autre n'avait pas.

- Le rang de l'étape ne déplie plus rien quand le guide n'en a qu'une : la
  pastille y ouvrait une liste d'un seul élément.

- Le signalement porte le succès entre parenthèses : « Île d'Otomaï › Les
  chasses de Crocodaille Dandi (Service de dépannage) ».

- Le signalement porte la zone et la quête, et non plus le rang de l'étape. Ce
  rang est une numérotation qui n'existe que dans cette fenêtre : le site ne
  numérote pas ses paragraphes, et le repère ne désignait donc rien pour qui
  reçoit le signalement. Le champ dit maintenant « Astrub › La découverte d'un
  destin ».

- La fenêtre de signalement épouse le formulaire : sa hauteur s'ajuste à ce
  qu'il mesure, sa largeur ne s'étire plus et le bouton d'agrandissement
  disparaît. Elle s'appelle « Remonter une erreur sur Papycha ».

- Le bouton de signalement ne paraît plus que sur un guide. Le site ne met de
  formulaire qu'en pied d'article, et n'en a pas de général : ailleurs, le
  bouton menait à la page de contact, ce qui n'était pas ce qu'il promettait.

- L'icône des quêtes rejoint la teinte neutre des trois autres lignes de
  l'accueil des guides, où elle était la seule en couleur.

- La croix qui fermait la fenêtre d'un compte devient un carré, en paire avec le
  triangle de lecture. Dans une liste, une croix veut dire « supprimer cette
  ligne » pour à peu près tout le monde ; rien n'est supprimé ici, ni le compte,
  ni son profil Android, ni le jeu sur le téléphone. L'infobulle le dit
  maintenant, et ne renvoie plus à une case à cocher retirée depuis.

- Le bouton d'association et celui des profils forment une paire. Ils partagent
  la même ligne et n'avaient rien en commun : l'un cadré, l'autre flottant sans
  bord, avec un plus en simple caractère qui passait pour de la ponctuation et un
  chevron de fonte à chasse fixe qui pendait sous le texte. Même dessin
  désormais, une marque d'accent à gauche, et le bleu sourd sous le doigt à la
  place d'une baisse d'opacité qui ne disait rien.

- Les aides montrent le chemin de menu comme la suite d'écrans qu'il décrit :
  chaque écran porte son nom en titre et, sur l'une de ses lignes, le libellé
  exact de ce qu'on y touche. C'était une ligne de texte à chevrons. Rien n'a
  été rédigé pour cela, les fiches de marque écrivant déjà leurs chemins sous
  cette forme.

- Le premier lancement ouvre le panneau sur l'onglet Appareils, avec la fenêtre
  d'association par-dessus quand aucun téléphone n'est connu. La fenêtre de mise
  en route disparaît : elle refaisait ce que le panneau fait déjà, et la
  refermer arrêtait l'application. L'état du téléphone en toutes lettres, qui
  n'existait que là, est repris dans le panneau.

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

- L'échelle de qualité allait à l'envers. Le débit était fixé par palier alors
  que la définition et la cadence, elles, changeaient : mesuré en bits par pixel
  et par image, ce qu'un encodeur reçoit vraiment, « maximale » en accordait
  cinq fois et demie moins que « basse » et rendait donc une image plus
  grossière en mouvement. Le débit suit maintenant la définition et la cadence
  réellement retenues. Sur le même écran et la même fenêtre, « maximale » passe
  de 0,029 à 0,089 bit par pixel, soit trois fois plus de bits par image.
- La cadence maximale descend de 120 à 60 images par seconde. Mesuré sur le jeu,
  il en rend trente-huit : les cent vingt ne servaient qu'à diviser par deux les
  bits accordés à chaque image qui existe vraiment.
- Le journal dit désormais, à chaque ouverture, la définition, la densité, la
  cadence et le débit retenus. Ils dépendent de la fenêtre, de l'écran et du
  palier, et ne se lisaient nulle part.
- Les succès retrouvent le rang que le site leur donne. Le site n'écrit pas le
  même nom aux deux endroits où il nomme un succès : l'intertitre dit « Brûler
  le pissenlit à la racine », « Fri Carré », « Etre plus royaliste que le roi »,
  la quête dit « par la racine », « Fri carré », « Étre » ; ailleurs c'est une
  coquille franche, « Globlitération » contre « Goblitération ». Le rang se
  prenait sur l'intitulé, si bien que trente des quatre-vingt-seize entrées de
  la liste des rangs ne désignaient aucun succès, et que quarante-neuf succès
  sur cent quinze n'en avaient pas. Il se prend maintenant sur les quêtes que
  l'intertitre coiffe : quatre-vingt-dix-sept rangs, aucun nom en trop, dix-huit
  succès sans rang. Rangés par ordre alphabétique en fin de zone, faute de tout
  signal : douze, contre trente-huit.
- Une page pouvait faire tomber l'application en trois lignes. Le pont est posé
  sur tout document que la fenêtre des guides charge, et la fenêtre lisait ce
  qu'il lui postait sans précaution : un message qui n'a pas le champ « kind »,
  ou qui n'est pas du texte, levait dans un gestionnaire d'événement, où
  personne ne rattrape. Mesuré sur neuf formes qu'une page peut poster, neuf
  levaient. La lecture est descendue dans le noyau, où elle se vérifie, et rend
  désormais un message ou rien.
- Nos fenêtres ne chargent plus que le site. Elles n'ont pas de barre
  d'adresse et portent notre cadre : tout lien qui sort du site part maintenant
  au navigateur, où l'on voit où l'on va. La fenêtre des pages liées prenait
  jusqu'ici n'importe quelle adresse, « file:// » compris, et ne retenait ni la
  navigation ni les ouvertures en fenêtre neuve.
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
