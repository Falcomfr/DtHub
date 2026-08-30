# Décisions d'architecture

Une entrée par décision structurante : le contexte, le choix retenu et ce
qu'il coûte. Les entrées ne sont pas réécrites ; si une décision est
remplacée, on ajoute une entrée qui l'annule explicitement.

---

## D1 - Le dépôt vit sur le disque Windows

**2026-08-29 - Acceptée**

Le développement se pilote depuis WSL, mais WPF ne se compile et ne s'exécute
que sous Windows. Compiler via `\\wsl.localhost\...` expose à des problèmes de
chemins UNC dans MSBuild et NuGet, à des lenteurs, et rend l'exécution de
l'application publiée bancale.

Le dépôt est donc à `C:\Dev\DTHub`, compilé par le SDK Windows appelé en
interopérabilité (`dotnet.exe`). Un lien symbolique `~/dev/DT Hub` le rend
accessible depuis WSL.

Conséquence : les accès disque depuis WSL passent par `/mnt/c` et sont plus
lents. C'est acceptable au regard du gain de fiabilité.

---

## D2 - Quatre projets, dépendances orientées vers le noyau

**2026-08-29 - Acceptée**

`Core` ne dépend de rien et concentre les modèles et les fonctions pures,
notamment les parseurs de sortie ADB. C'est ce qui rend l'essentiel testable
sans téléphone, sans réseau et sans Windows.

`Infrastructure` implémente les contrats de `Core` : exécution de processus,
ADB, scrcpy, Win32, persistance. `App` ne contient que de l'interface.

`Core` et `Infrastructure` ciblent `net10.0` et non `net10.0-windows` : les
appels Win32 se font par `DllImport`, qui ne réclame pas la cible Windows. Le
projet de tests reste ainsi compilable et exécutable partout, ce qui simplifie
l'intégration continue.

---

## D3 - Identité produit centralisée

**2026-08-29 - Acceptée**

Le nom « DT Hub » est provisoire. Il n'apparaît qu'à deux endroits dans le
code : `Directory.Build.props` côté MSBuild et `ProductInfo.cs` côté C#. Les
espaces de noms `DtHub.*` restent des identifiants techniques et ne suivent
pas les changements de nom commercial.

---

## D4 - ADB et scrcpy téléchargés depuis leurs sources officielles

**2026-08-29 - Acceptée**

Le contrat de licence du SDK Android n'autorise pas la redistribution des
platform tools. ADB n'est donc pas embarqué : il est téléchargé depuis l'URL
officielle Google au premier lancement, vérifié, et installé dans le dossier
de données de l'utilisateur. Il est ensuite toujours invoqué par chemin
absolu, jamais via le `PATH`, pour ne pas dépendre d'une installation tierce
ni entrer en conflit avec elle.

scrcpy est sous Apache 2.0 et pourrait être embarqué. Il est tout de même
téléchargé, par le même mécanisme : l'installateur reste léger et il n'y a
qu'un seul chemin de mise en place à maintenir et à tester. Le `LICENSE.txt`
fourni dans l'archive amont est extrait tel quel.

L'archive Windows de scrcpy contient sa propre copie d'`adb.exe`. Elle n'est
pas utilisée : DT Hub garde celle de Google, dont il maîtrise la version, et
l'indique à scrcpy par la variable d'environnement `ADB`.

Conséquence : le premier lancement nécessite une connexion internet. C'est
annoncé à l'utilisateur.

---

## D5 - Lancer sur un utilisateur Android secondaire sans forker scrcpy

**2026-08-29 - Acceptée, vérifiée sur scrcpy v4.1**

Le besoin : ouvrir une application sur l'utilisateur Android 0, 10, 999 ou
n'importe quel autre identifiant valide, chacun dans sa propre fenêtre.

L'option `--start-app` de scrcpy ne prend pas d'identifiant d'utilisateur.
Plutôt que de maintenir un fork du serveur scrcpy, qui imposerait une chaîne
de compilation Java et Android SDK et un suivi permanent de l'amont, la
stratégie retenue est :

1. demander à scrcpy de créer un afficheur virtuel (`--new-display`) ;
2. lire l'identifiant de cet afficheur dans la sortie de scrcpy ;
3. lancer l'application nous-mêmes via ADB, avec l'utilisateur voulu :
   `am start --user <id> --display <displayId> -n <composant>`.

Cette voie ne modifie ni scrcpy ni l'application Android ciblée, et accepte
n'importe quel identifiant d'utilisateur entier.

Vérifications effectuées sur scrcpy v4.1 :

- `--new-display=<taille>/<densité>` est documenté et crée bien un afficheur
  virtuel détruit à la fermeture ;
- `NewDisplayCapture.java` journalise `New display: <taille>/<densité>
  (id=<identifiant>)`, relayé au client, donc lisible sur la sortie du
  processus ;
- `--start-app` ne prend aucun identifiant d'utilisateur, ce qui confirme
  qu'il ne peut pas répondre au besoin seul ;
- `--no-vd-system-decorations` permet de partir d'un afficheur vide plutôt que
  du lanceur du téléphone.

Reste à vérifier sur matériel réel, en phase 13 : le comportement sur un
utilisateur secondaire arrêté, et les applications qui refusent de s'ouvrir
sur un afficheur secondaire.

Le service de lancement reste conçu avec des stratégies interchangeables, de
sorte qu'un repli par patch du serveur scrcpy demeure possible. La procédure
qu'imposerait un tel patch est écrite dans
`third_party/scrcpy/MODIFICATIONS.md`.

---

## D6 - Pas d'icônes réelles d'applications dans le cycle 0.x

**2026-08-29 - Acceptée, à rouvrir**

Le sélecteur d'applications gagnerait à montrer les vraies icônes. Aucune voie
raisonnable ne le permet aujourd'hui :

- ADB n'expose pas les icônes. `dumpsys package` et `cmd package
  resolve-activity` ne rendent que des identifiants de ressource, pas les
  images.
- `scrcpy --list-apps` donne les noms mais pas les icônes.
- Extraire l'icône de l'APK supposerait de récupérer le fichier, qui pèse
  souvent des centaines de mégaoctets, puis de décoder les ressources
  Android. Le coût est sans rapport avec le bénéfice.
- Un assistant installé sur le téléphone est exclu : DT Hub ne modifie pas
  l'appareil au-delà de ce que fait scrcpy.

Le sélecteur affiche donc une pastille portant l'initiale du nom, colorée de
façon déterministe à partir du nom de paquet. Deux applications se
distinguent, ce qui est l'usage réel de l'icône dans une liste.

Ce que la décision préserve : les vrais noms d'applications, qui portent
l'essentiel de la reconnaissance, sont bien récupérés.

À rouvrir si scrcpy publie les icônes, ou si une extraction ciblée d'entrée
d'APK devient possible sans transférer le fichier entier.

---

## D7 - Raccourcis par RegisterHotKey, activés seulement sur nos fenêtres

**2026-08-29 - Acceptée**

Le cahier des charges demande que Ctrl+Tab reste disponible dans les autres
logiciels. Deux voies existaient :

- un crochet clavier de bas niveau, qui voit toutes les frappes du système et
  décide au cas par cas de les avaler. Techniquement souple, mais
  disproportionné : le programme observerait tout ce que l'utilisateur tape,
  et un tel crochet est indiscernable d'un enregistreur de frappe, pour un
  antivirus comme pour un lecteur du code ;
- `RegisterHotKey`, qui ne capte que les combinaisons déclarées, associé à un
  enregistrement conditionnel.

C'est la seconde qui est retenue. Un crochet d'événement système signale les
changements de fenêtre active ; aucune frappe n'y transite. Quand une fenêtre
de DT Hub prend le focus, les raccourcis sont enregistrés ; quand elle le
perd, ils sont retirés et les combinaisons reviennent aux autres logiciels.

Conséquences assumées :

- un raccourci déjà pris par un autre logiciel est refusé par Windows. Le
  refus est détecté et remonté à l'utilisateur plutôt que d'échouer en
  silence ;
- les raccourcis ne fonctionnent pas depuis une autre application, ce qui est
  précisément l'effet recherché.

---

## D8 - Pas de mode de globalisation invariant

**2026-08-29 - Acceptée**

`InvariantGlobalization` avait été activé par réflexe, pour réduire la taille
de la publication. C'est incompatible avec WPF : la liaison de données appelle
`XmlLanguage.GetSpecificCulture()`, qui échoue sans données de culture, et
chaque liaison lève alors une exception. Constaté au premier lancement réel.

Le réglage est retiré. L'interface est en français et affichera des dates et
des nombres localisés : les données de culture sont de toute façon
nécessaires.

Enseignement retenu : un réglage de publication ne se valide pas à la
compilation. Il faut lancer l'application.

---

## D9 - Une application dédiée à DOFUS Touch, pas un outil générique

**2026-08-29 - Acceptée, remplace le cadrage initial**

Le cahier des charges de départ demandait un outil générique, capable de
lancer n'importe quelle application Android. À l'usage, cette généralité ne
servait personne : elle imposait un catalogue d'applications, un sélecteur, des
profils de lancement et une navigation à six pages, pour un besoin qui tient
en une phrase, ouvrir plusieurs comptes du même jeu.

L'application est donc recentrée. La découverte ne cherche qu'un paquet, celui
du jeu, et rend une instance par profil Android où il est installé. Le paquet
reste un réglage, pour survivre à un changement côté éditeur, mais rien dans
l'interface ne propose de choisir une autre application.

Ce que la décision supprime : profils de lancement, catalogue d'applications,
fournisseur de noms d'applications, favoris, vues Favoris et Système, barre de
navigation, presets de taille et leurs raccourcis.

Ce qu'elle garde intact : tout le noyau technique, qui n'a jamais rien su du
jeu. ADB, l'appairage, les profils Android, les sessions scrcpy et la gestion
des fenêtres sont inchangés.

Limite non négociable, indépendante de cette décision : aucune automatisation
de jeu. Une entrée utilisateur correspond à une action, sur un compte, et à
une seule.

---

## D10 - Un fichier unique, pas d'installateur

**2026-08-29 - Acceptée, remplace la décision d'empaqueter avec Velopack**

L'usage visé est personnel : un fichier qu'on lance. Mesuré sur cette base de
code, la publication autonome en fichier unique donne un `DtHub.exe` de 60 Mo,
runtime .NET compris, qui démarre sans installation ni droits administrateur.

Velopack et l'installateur sont abandonnés. Mettre à jour revient à remplacer
le fichier.

Deux conséquences assumées :

- le premier lancement nécessite une connexion internet, le temps de
  télécharger ADB et scrcpy. La licence du SDK Android interdit d'embarquer
  ADB, donc ce téléchargement ne peut pas disparaître ;
- Windows affiche un avertissement SmartScreen au premier lancement, le
  fichier n'étant pas signé. Un certificat Authenticode le supprimerait, pour
  un coût annuel qui ne se justifie que si le programme est distribué
  largement.

---

## D11 - Deux fenêtres, et un configurateur qui se cache

**2026-08-29 - Acceptée**

L'application n'a pas d'écran d'accueil permanent. Une fenêtre de mise en
route apparaît une seule fois, à la première utilisation, pour demander
quelles instances ouvrir. Ensuite il ne reste qu'un configurateur flottant,
masqué et rappelé par un raccourci affiché dans son propre bandeau.

Les fenêtres de jeu se superposent exactement, ancrées sur une des neuf
positions d'une grille. Le configurateur se pose à l'opposé de ce bloc, pour
ne pas le recouvrir.

Fermer le configurateur ne quitte pas l'application : le jeu continue. Le
mode d'arrêt de WPF est donc explicite, et un bouton Quitter existe dans le
configurateur.

---

## D12 - Android seulement, iOS est hors d'atteinte

**2026-08-29 - Acceptée**

La question a été posée : peut-on ajouter un iPhone ? Non, et ce n'est pas un
manque d'effort.

Le fonctionnement de DT Hub repose sur trois briques : créer un écran virtuel
supplémentaire sur l'appareil, y lancer une application, et lui transmettre
clavier et souris. Android les expose au compte `shell` via ADB, ce dont
scrcpy se sert.

iOS n'en expose aucune. Il n'y a pas d'écran secondaire créable, l'injection
d'entrées demande un outil de test signé à réinstaller régulièrement, et
surtout une même application ne peut pas être installée deux fois sur un
iPhone. Or c'est précisément ce qui donne son intérêt à l'outil : plusieurs
comptes sur un même téléphone.

Même en réussissant le mirroring d'écran, qui est partiellement faisable, il
n'y aurait qu'un compte par iPhone. L'onglet de choix de plateforme a donc été
retiré ; une phrase l'explique dans la fenêtre d'ajout.

---

## D13 - Ne pas construire une liste d'appareils à partir des seules annonces réseau

**2026-08-29 - Acceptée, tirée d'une observation sur matériel**

La fenêtre d'ajout listait les téléphones à partir des annonces mDNS. Elle
restait désespérément vide alors qu'un téléphone était connecté.

Vérification faite avec ADB : **un téléphone cesse d'annoncer
`_adb-tls-connect._tcp` dès qu'une connexion est établie**. Une liste bâtie
sur les seules annonces se vide donc exactement quand tout fonctionne, ce qui
est le pire moment pour paraître vide.

La liste est désormais construite à partir des appareils connus d'ADB,
connectés ou non, complétée par les annonces qui ne correspondent à aucun
d'eux. Le bouton de connexion agit selon l'origine de la ligne : connexion
directe pour une annonce, reconnexion complète pour un appareil mémorisé.

## D14 - Les fenêtres de mirroring vivent et meurent avec l'application

Une fin anormale de DT Hub, un plantage ou un arrêt par le gestionnaire des
tâches laissaient les processus scrcpy en vie. Au lancement suivant, ces
fenêtres n'étaient pas reconnues et de nouvelles venaient s'y ajouter : après
quelques incidents, l'écran portait plusieurs jeux de fenêtres identiques. Le
cas a été observé avec huit fenêtres pour deux comptes.

Deux garde-fous, complémentaires.

Chaque processus lancé est rattaché à un objet de travail Windows portant
l'option de terminaison à la fermeture. Quand le dernier descripteur se ferme,
ce qui arrive même si le processus est tué, Windows arrête les enfants. Aucune
élévation n'est nécessaire, et rien n'est demandé à l'utilisateur.

Au démarrage, les processus issus de notre propre copie de scrcpy sont arrêtés
avant tout lancement. Ce second filet couvre ce que le premier ne peut pas :
les orphelins laissés par une version antérieure, et le cas où le rattachement
échoue. La comparaison porte sur le chemin complet de l'exécutable : une copie
de scrcpy installée par l'utilisateur n'est jamais touchée.

Vérifié : huit orphelins ramassés au démarrage, et zéro survivant après un
arrêt brutal de l'application.

## D15 - L'afficheur naît à la hauteur du plus grand écran (corrigée par D16)

Le jeu laissait une bande noire dès qu'une fenêtre dépassait 1416 pixels de
haut. Ce chiffre a été pris pendant des semaines pour une limite du jeu, et
tout a été bâti autour : plafonner la fenêtre, verrouiller son rapport, mettre
l'image à l'échelle. Aucun de ces contournements n'était satisfaisant, et
chacun retirait quelque chose à l'utilisateur.

La mesure a montré que ce n'en était pas une. Sur un afficheur né en 2760x2000,
le jeu remplit les 2000 pixels. Né en 2820x2160, il remplit 2076 en plein
écran. La règle réelle est ailleurs : **le jeu ne dessine jamais au-delà de la
hauteur qu'avait son afficheur quand son activité a démarré.** Descendre
ensuite ne lui pose aucun problème, remonter non plus tant qu'on reste sous
cette hauteur. Nous lui donnions nous-mêmes 1416, et il s'y tenait.

Trois mesures ont écarté les explications concurrentes. Rétrécir puis
réagrandir remplit toujours : ce n'est pas le redimensionnement qui abîme.
Toucher l'écran après une naissance à 1416 fait réapparaître la bande, alors
que la même manipulation après une naissance à 2160 ne la fait pas : c'est
bien la hauteur de naissance qui compte, et une interaction suffit à révéler
le plafond. Trois secondes suffisent entre le démarrage de l'activité et le
premier redimensionnement.

L'afficheur naît donc à la hauteur du plus haut des écrans, et la fenêtre est
garée hors écran le temps de sa naissance, pour ne pas paraître à cette taille
avant d'être ramenée à la sienne. La largeur reste libre, elle l'a toujours
été.

Vérifié sur un Xiaomi 13T, sans aucune bande et sans rechargement : 978x644,
1178x744, 1578x944, 2378x1144, 2820x1844 et 3818x2032.

La densité de l'afficheur, restée à 240 pour toutes ces mesures, n'entre pas
en jeu. L'hypothèse d'un plafond exprimé en points d'interface, 1416 pixels à
240 ppp valant exactement 944 dp, était séduisante et fausse.

## D16 - Le jeu fige la hauteur de sa mise en page à son initialisation

D15 concluait que le jeu ne dessine jamais au-delà de la hauteur de naissance
de son afficheur, et faisait naître celui-ci à la hauteur du plus grand écran.
La mesure était juste, la conclusion trop étroite, et le remède a rendu les
choses pires : né en 2160 puis ramené à 1192, le jeu dessinait toujours sa
mise en page de 2160 et l'image se retrouvait **rognée**, on perdait le bas et
la droite.

La règle exacte est plus simple. **Le jeu fige la hauteur de sa mise en page
quand il s'initialise, sur la hauteur qu'a l'afficheur à cet instant. La
largeur, elle, se recalcule à tout moment.** Trois mesures sur un Xiaomi 13T,
jeu arrêté avant chaque essai pour qu'il s'initialise vraiment :

| Afficheur à la naissance | Fenêtre ensuite | Résultat |
|---|---|---|
| 2280x1192 | inchangée | mise en page complète, pied de page compris |
| 2280x1192 | 1378x844 | mise en page refaite en largeur, rognée en bas |
| 1378x844 | 2280x1192 | mise en page refaite en largeur, bande noire en bas |

Ce que D15 prenait pour un plafond de 1416 pixels n'était que la hauteur que
nous donnions nous-mêmes à l'afficheur.

Deux conséquences.

L'afficheur naît à la taille **exacte de la zone client** de la fenêtre, cadre
déduit. Le cadre est mesuré par `AdjustWindowRectExForDpi` avant qu'aucune
fenêtre n'existe : compter le rectangle extérieur rendrait l'afficheur trop
haut d'une barre de titre, et l'image serait rognée d'autant. Redimensionner
la fenêtre après coup pour corriger ne rattrape rien : mesuré, le jeu s'était
déjà initialisé.

Aucun rattrapage n'est possible sans recharger le jeu. Une variation de deux
pixels, essayée et mesurée, ne provoque aucune remise en page : la fenêtre
Android du jeu suit pourtant parfaitement l'afficheur, `dumpsys window` le
montre, et c'est bien la mise en page interne du jeu qui ne bouge plus.

Reste donc une limite assumée : **changer la hauteur d'une fenêtre en cours de
partie dégrade l'image**, en bande si on l'agrandit, en rognage si on la
réduit. La largeur reste libre, d'où le nom du mode. Seul le mode à définition
fixe, où l'image est mise à l'échelle, accepte toute hauteur sans rien perdre.

## D17 - Un seul mode d'affichage : l'image mise à l'échelle de la fenêtre

D16 établit que le jeu fige la hauteur de sa mise en page à son initialisation.
Le mode « largeur libre », qui faisait épouser la fenêtre par l'afficheur, ne
pouvait donc pas tenir sa promesse : élargir marchait, mais changer la hauteur
rognait l'image ou laissait une bande, et il n'existe aucun moyen de le
rattraper sans recharger le jeu, ce qui déconnecterait le compte en pleine
partie.

Le réglage est retiré, et avec lui toute la branche flexible : plus de
`--flex-display`, plus de plafond de hauteur, plus de fenêtre garée hors écran
le temps de l'ouverture.

L'afficheur virtuel prend désormais la définition entière de l'écran retenu, et
l'image est mise à l'échelle de la fenêtre. Toute taille est alors acceptée,
l'image reste complète et juste, et le plein écran est net. La fenêtre garde le
rapport de l'écran : tirer un bord ajuste l'autre, ce qui est le prix, et le
seul.

La taille transmise à scrcpy est celle de la zone client, cadre déduit :
scrcpy dimensionne sa fenêtre par l'intérieur.

Vérifié sur un Xiaomi 13T, image complète et sans bande à 978x550, 1378x775,
1578x888, 2378x1338 et 3818x2130.

Le fichier de réglages passe en version 5 : `freeWidthResize` disparaît, sans
que rien ne soit à décider pour l'utilisateur.

## D18 - La définition de l'afficheur suit la taille de la fenêtre, par paliers

L'image étant mise à l'échelle de la fenêtre (D17), un afficheur toujours pris
à la définition de l'écran rendait l'interface du jeu minuscule dans une petite
fenêtre : sur un écran 4K, une fenêtre de 550 pixels de haut réduisait l'image
d'un facteur quatre. À l'inverse, un afficheur toujours petit l'aurait rendue
énorme et floue en plein écran.

La définition suit donc la fenêtre, mais par paliers de 180 pixels de haut,
`DisplayLadder` retenant le premier palier au-dessus d'elle. Deux raisons de ne
pas coller au pixel près : la définition est figée pour toute la session, et
une échelle qui changerait à chaque relance serait déroutante. Le palier étant
toujours au-dessus, l'image est réduite et jamais agrandie, donc nette.

Mesuré sur un écran 3840x2160 : une fenêtre de 700 de haut ouvre un afficheur
1280x720 et l'interface reste parfaitement lisible ; une fenêtre plein écran
ouvre un afficheur 3840x2160 et montre le maximum de terrain.

La définition ne change qu'à l'ouverture d'une session. Redimensionner
longuement une fenêtre puis relancer l'instance la fait passer au palier
correspondant.

## D19 - Le configurateur ne replace plus les fenêtres en relisant ses réglages

Défaut ancien, trouvé en instrumentant le placement. `OnGameAnchorChanged` et
`OnPreferredMonitorChanged` déclenchaient un replacement sans passer par le
garde-fou de chargement, contrairement à l'enregistrement. Relire les réglages
au démarrage replaçait donc toutes les fenêtres sur l'ancrage et effaçait leur
géométrie mémorisée, avant même que l'utilisateur ait touché à quoi que ce
soit.

C'est ce qui faisait revenir les fenêtres empilées au même endroit à chaque
lancement, quoi qu'on ait fait de leur position la veille.

## D20 - Une définition de repli quand l'encodeur de l'appareil plafonne

Rien du mécanisme d'affichage ne dépend de l'appareil : l'afficheur virtuel est
créé à la définition et à la densité que nous demandons, sans aucun rapport
avec l'écran du téléphone ou de la tablette. Un appareil en 1220x2712 peut
parfaitement porter un afficheur 3840x2160 en paysage.

Une seule dépendance au matériel subsiste : **l'encodeur vidéo annonce une
définition maximale**, et elle varie beaucoup. Relevée dans
`/vendor/etc/media_codecs_c2.xml` du Xiaomi 13T, elle vaut 160x128 à 7680x4320
pour l'encodeur AVC matériel. Beaucoup d'appareils d'entrée de gamme ou plus
anciens s'arrêtent à 1920x1088, et une tablette modeste refuserait alors la
définition demandée sur un grand écran.

Plutôt que de renoncer, une session refusée est retentée une fois en 1920x1080,
définition qu'aucun appareil capable de faire tourner scrcpy ne refuse. Le
journal en garde trace. L'affichage y perd en finesse sur un grand écran, rien
d'autre.

## D21 - L'ordre des instances est global, les appareils ne se trient plus

Les instances étaient groupées par appareil et ne franchissaient jamais cette
frontière. Elles se trient désormais librement entre elles, et le nom du
téléphone n'apparaît qu'aux endroits où il change : deux instances qui se
suivent n'en portent qu'un, un téléphone coupé en deux morceaux en reçoit un
par morceau. Ce qui vaut pour l'appareil lui-même, comme rompre l'association,
ne paraît que sur son premier morceau.

`DeviceOrder` disparaît des réglages : le rang de chaque instance porte tout
l'ordre, et une liste d'identifiants d'appareils ne saurait pas exprimer un
ordre entrelacé. Deux sources dont l'une ne peut pas représenter l'autre, c'est
la garantie d'une dérive.

Les déplacements se disent par clés et non plus par décalage. La liste affichée
ne montre que les appareils joignables alors que les réglages portent toutes
les instances : un décalage compté sur les positions visibles désignait la
mauvaise destination dès qu'une instance cachée s'intercalait.

Conséquence assumée : le parcours au clavier suit ce nouvel ordre, entrelacé
compris.

## D22 - Ce qui rouvre est un état explicite, pas l'état du moment où l'on quitte

L'ensemble des instances à rouvrir était recalculé à la sortie, à partir des
sessions vivantes à cet instant. Fermer une fenêtre à la main la retirait donc
de l'ensemble, ce qui est l'inverse de ce qu'on attend d'un geste banal.

Trois écritures, et trois seulement : lancer une instance l'y met, le bouton
« Fermer » l'en retire, et la case de la fenêtre de mise en route en décide au
premier lancement. Quitter l'application, fermer une fenêtre de jeu ou perdre
le téléphone n'y touchent pas.

## D23 - Relancer redémarre le jeu, pas la fenêtre

La relance fermait la session scrcpy et la rouvrait : la fenêtre disparaissait
et revenait. Elle se contente maintenant d'arrêter le jeu côté Android et de le
redémarrer sur le même afficheur, la session et sa fenêtre étant conservées. Un
repli ferme et rouvre tout si le redémarrage court échoue.

Un cas reste indétectable et figure dans l'infobulle du bouton : si le
démarrage réussit mais que le jeu se ferme juste après, la fenêtre reste
ouverte sur un afficheur vide.

L'afficheur étant conservé, changer de palier de définition passe désormais par
« Fermer » puis « Lancer », et non plus par « Relancer ». Cela corrige la
dernière phrase de D18.

## D24 - Redimensionner garde la position relative dans l'écran

Les raccourcis de taille et le curseur gardaient le coin haut-gauche puis
reprenaient la fenêtre dans l'écran : elle était poussée dès qu'elle
grandissait près d'un bord. La part d'espace libre à gauche et au-dessus reste
maintenant constante, si bien qu'une fenêtre collée à un bord y reste, une
fenêtre au milieu grandit autour de son centre, et une fenêtre dans un coin
grandit depuis ce coin.

Le plein écran retient d'où vient chaque fenêtre et le lui rend en sortant, au
lieu de les empiler sur l'ancrage, et il couvre l'écran qui porte la fenêtre.

## D25 - Un réglage de qualité, et le vrai coût qui n'était pas l'image

Trois valeurs, moyenne par défaut et identique au comportement d'avant. Basse
allège tout : trente images par seconde, débit réduit, définition d'afficheur
bornée à 720, et sondages espacés.

Le poste le plus lourd n'était pas l'image mais la redécouverte des instances,
qui demandait deux commandes par profil et par appareil toutes les trois
secondes, dès que le configurateur était visible. Elle ne se refait plus que si
l'ensemble des appareils a changé, ou après un long moment ; lister les
appareils, bon marché, garde son rythme.

Les images par seconde et le débit ne viennent plus que de la qualité : deux
sources pour un même réglage auraient fini par diverger.

## D26 - Le réglage d'écran disparaît, l'afficheur suit la fenêtre

Chaque fenêtre retrouve la place où elle a été laissée, second écran compris :
désigner un écran de référence ne servait plus.

Un piège allait avec : la définition de l'afficheur se calculait sur cet écran
unique, si bien qu'une fenêtre mémorisée sur un second écran de forme
différente naissait mal formée. Elle se calcule désormais sur l'écran où la
fenêtre va réellement s'ouvrir, instance par instance.

L'ancrage reste, pour une fenêtre sans géométrie mémorisée et pour la grille
3x3 qui regroupe volontairement toutes les fenêtres.

## D27 - Une fenêtre peut sortir des placements automatiques

Demande initiale : un petit bouton sur la barre de titre de la fenêtre de jeu.
Impossible tel quel, cette barre appartenant à scrcpy. Deux replis ont été
proposés, une pastille flottante posée par-dessus et une marque dans le titre ;
l'utilisateur a retenu le plus simple, une case sur la ligne de l'instance,
cochée par défaut.

Décochée, la fenêtre est ignorée par le parcours au clavier, le replacement, le
côte à côte et les changements de taille. Elle s'ouvre, se ferme et se souvient
de sa place comme les autres : c'est un retrait des placements, pas une mise à
l'écart.

## D28 - Un rangement côte à côte

Le replacement empile toutes les fenêtres au même endroit, ce qui sert à en
consulter une à la fois. Le rangement côte à côte partage l'écran en deux
moitiés, la fenêtre active à droite, ce qui sert à en suivre deux.

Au-delà de deux, les suivantes se rangent derrière celle de gauche : l'écran ne
se partage plus utilement, et un empilement reste préférable à des fenêtres
devenues trop étroites.

La hauteur suit le rapport de l'afficheur. La remplir davantage laisserait une
bande, l'image étant mise à l'échelle.
