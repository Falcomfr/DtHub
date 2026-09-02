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

## D29 - Un suivi de quêtes adossé à papycha.fr

Jouer plusieurs comptes suppose de suivre plusieurs quêtes, et le guide de
référence est papycha.fr. Une fenêtre de plus, toujours au-dessus, ouverte par
Ctrl+Q, affiche leur page telle quelle dans un navigateur embarqué.

C'est un ajout d'affichage. Rien n'automatise le jeu : la limite du projet
n'est pas approchée.

Leur page n'est pas découpée, elle est cadrée : la vue est fixée sur l'article
et ne peut pas remonter au-dessus, mais rien n'est retiré, ni bandeau ni
signature. La source est citée en clair, avec un bouton qui ouvre la page dans
le vrai navigateur. Leur logo seulement s'ils l'accordent.

Le catalogue est bâti en huit requêtes sur leur API REST, mises en cache sept
jours. Les champs sont demandés nommément, sans le corps des articles : 650 Ko
au lieu de 13 Mo.

Le succès dont une quête fait partie n'est pourtant lisible que dans ce corps.
Le lire à chaque indexation, sur chaque poste, coûterait ces 13 Mo par semaine
pour une information qui ne bouge qu'aux mises à jour du jeu. Il est donc relevé
une fois, par `build/extract-successes.py`, et livré avec l'application dans
`assets/quest-successes.json` : 37 Ko. Les intertitres des pages de rubrique
restent lus à chaque indexation et rattrapent les quêtes ajoutées depuis.

Mesuré : les intertitres seuls rattachent 380 quêtes, la carte 505, et le
catalogue en rattache 498 sur 782. Les autres n'ont pas de succès, ce que
confirme la liste officielle du site, qui n'en annonce que 475 au total.

La fenêtre n'emploie pas AllowsTransparency. Un WebView2 est une surface native
et ne se dessine pas dans une fenêtre transparente.

## D30 - La compatibilité est vérifiée, non supposée

L'application était bâtie et éprouvée sur un seul appareil. Rien n'y était
faux, mais plusieurs exigences n'étaient écrites nulle part dans le code : le
code les supposait, et l'utilisateur d'un autre appareil découvrait l'écart
sous la forme d'une attente de trente secondes suivie d'un message deviné.

Trois principes en sont sortis.

**Ce qu'on sait déjà, on ne le redécouvre pas par l'échec.** Le niveau d'API
était lu à la découverte et comparé nulle part. Un appareil trop ancien va
maintenant au refus immédiat, en disant sa version.

**Un repli sait ce qu'il répare.** Le repli de définition se déclenchait sur
n'importe quel échec et ne connaissait qu'une marche. Les refus sont rangés en
catégories et seuls ceux qu'une définition plus modeste peut réparer sont
retentés, en descendant les paliers.

**Un repli silencieux ment par omission.** Quand la liste des profils Android
n'est pas lisible, l'appareil rendait une instance et ressemblait trait pour
trait à un appareil qui n'en a qu'une. Le repli est dit.

Ce qui reste supposé est dit ici plutôt que caché : que « am start --user N
--display D » soit autorisé sur un profil secondaire n'est vérifié que sur le
Xiaomi 13T, et les chemins de menu des fiches de marques ne sont vérifiés que
pour Xiaomi.

## D31 - Windows x64 seulement, et pourquoi

La publication vise win-x64. Windows sur ARM n'est pas visé, non par choix mais
parce que scrcpy n'y est pas distribué : porter DT Hub sans lui ne donnerait
rien à ouvrir.

Le socle est Windows 10 version 1809, imposé par WebView2 et par .NET 10, et
déclaré dans le manifeste de l'application. Ce manifeste déclare aussi la
conscience de la mise à l'échelle écran par écran : sans elle, Windows livre
les coordonnées virtualisées du moniteur principal, et les fenêtres déplacées
vers un écran d'un autre facteur sont redimensionnées d'office.

## D32 - Ce que la fenêtre de quêtes emprunte au site, et ce qu'elle en refait

Le site range et nomme pour un lecteur qui arrive par un moteur de recherche.
L'application affiche à quelqu'un qui joue. Les deux ne demandent pas la même
chose, et trois écarts ont été assumés.

**Les noms.** Une rubrique perd son préfixe « Quêtes » quand un article le suit,
parce que ce qui reste est alors un lieu. Sans article, le mot fait partie du
nom : « Quêtes principales » ne désigne pas un endroit.

**L'ordre.** Le tableau du site classe ses pages ; il ne suit pas la progression
du jeu. L'ordre affiché est écrit à la main, d'Albuera à Frigost, et ce qui ne
relève pas de la progression passe sous un intertitre. Une zone que la table
ignore se range en fin de progression, jamais au milieu.

**Les voisines d'une quête.** Le site publie en pied d'article une chaîne de
prérequis qui saute d'un succès à l'autre et se ramifie. Ce n'est pas ce qu'on
parcourt : la précédente est celle qu'on voit au-dessus dans la liste du succès,
la suivante celle d'en dessous.

**Le départ.** C'est une étape, la première, et non une manière de décrire le
premier paragraphe du guide. Les deux avaient été confondus, en supposant que ce
paragraphe disait où commencer ; relevé sur seize guides, treize ouvrent sur un
préambule qui n'a rien à voir, « Cette quête est répétable », « La quête se lance
à la suite de la précédente », « Divers : ». Le bandeau annonçait donc une
adresse au-dessus d'un texte parlant d'autre chose. Le départ a désormais son
rang, ancré en haut du guide, et chaque autre étape est résumée par son propre
paragraphe.

Ce résumé se compose, pour le départ, des métadonnées de la quête, renseignées
sur près de neuf quêtes sur dix, plutôt que de la prose du site. Les autres
passent par des règles sur le texte ; quand aucune ne s'applique, la première
phrase raccourcie. Jamais de vide : une étape sans résumé laisserait croire
qu'il n'y a rien à faire.

**Les voisines, fin de série.** La suite d'un succès ne pend pas toujours à sa
dernière quête : sur les cent quinze succès, la première quête de douze d'entre
eux a pour prérequis une quête du milieu du succès précédent. « Médiation
expéditive » se prolonge depuis sa cinquième quête sur six, si bien que la
sixième n'avait aucune suite. On cherche donc dans tout le succès courant, ce qui
en pourvoit sept ; deux en ouvrent plusieurs et restent muets.

**Les voisines, suite.** La liste d'un succès s'arrête à ses bornes, et le site
ne s'y arrête pas : à Albuera, « Bien débuter » mène à « Une arrivée
mouvementée », qui mène à « Le début des problèmes », laquelle ouvre un succès.
Quand la liste ne dit rien, le graphe des prérequis prend le relais - cent
soixante-huit suivantes et cent quatre-vingt-dix-sept précédentes gagnées. Une
seule candidate, sinon rien : treize suites se ramifient, et en désigner une
mentirait. Le bouton nomme la série d'arrivée dès qu'on en change de succès ou
de zone, faute de quoi on croirait poursuivre la même.

**Les icônes.** Elles disent la nature d'une ligne, non ce que le clic fera :
deux branches se déplient de la même façon sans désigner la même chose. Une
épingle pour un endroit du monde, un marque-page pour une famille de quêtes -
alignements, saisons, répétables, et « Quêtes principales », qui ouvre la
progression sans être un lieu. Cinq teintes, pas une de plus : au-delà, une
liste de soixante lignes se lit comme un vitrail. Elles ne réemploient pas les
couleurs d'état, qui annoncent autre chose.

Deux dessins ont été refaits après mesure à l'écran, la taille utile étant de
quinze pixels : un parchemin à deux rouleaux s'y refermait en tache, et une clé
se lisait comme une loupe à quelques centimètres du champ de recherche.

Le bouton « Ouvrir dans le navigateur » suit la même règle : il ouvre ce que la
fenêtre montre. La page qui énumère les zones quand on les parcourt, celle d'une
rubrique quand on y est entré, la quête sinon. Le tableau de « Quêtes » ne nomme
ses rubriques ni comme les catégories du site ni comme nous : « Quêtes de
Frigost » d'un côté, « Île de Frigost » de l'autre. Le rapprochement se fait
donc sur le nom d'abord, treize rubriques sur vingt-cinq, puis sur le contenu,
qui ne ment pas, ce qui en donne vingt-deux. Les trois qui restent n'ont pas de
page à elles, et l'on retombe alors sur celle qui les énumère toutes.

Enfin, un lien cliqué dans un guide. Le catalogue tranche : s'il connaît
l'adresse, la fenêtre la suit sur place et se remet à jour, exactement comme si
l'on avait pressé « précédente » ; les cinq cent onze liens de quête de la
colonne des prérequis passent par là. Sinon elle ouvre une fenêtre à part,
parce qu'elle tient un état — titre, succès, étapes, voisines — qu'une
navigation qu'elle n'a pas demandée rendrait faux sans qu'elle le sache.

## D33 - Cadrer une page du site sans la casser

Le site est fait pour un grand écran et pour quelqu'un qui le parcourt. Nos
fenêtres sont étroites et affichent une seule page à la fois. Le pont enlève
donc le décor, et quatre règles gouvernent ce retrait.

**Le masquage passe par une feuille, pas par des styles en ligne.** Le greffon
du site rappelle certains éléments au premier défilement avec un fondu, et un
fondu réaffecte `style.display`, ce qui perd le `!important` d'un style en
ligne. Les éléments reçoivent un attribut, et une règle de notre feuille les
masque : une déclaration `!important` de feuille l'emporte sur un style en ligne
ordinaire, l'inverse n'est pas vrai.

**Le point d'ancrage est le contenu, pas une liste de choses à retirer.** On
part du contenu de la page et l'on masque ses frères en remontant : la règle ne
nomme rien et survit aux changements du thème. Le contenu, c'est
`.entry-content` quand la page est un article, sinon le repère de contenu du
thème. Sans ce recours, la carte, qui n'est pas un article, gardait l'en-tête et
la bannière du site, soit le quart haut de la fenêtre.

**Ce qui flotte n'est retiré que si l'on sait où est le contenu.** Sans ancrage,
masquer tout ce qui est fixe ôterait à la carte ses propres commandes.

**Ce qui arrive après coup est rattrapé.** Un observateur de mutations réapplique
le cadrage : un nombre fini de passes ne suffit pas quand le site pose des blocs
quand il veut.

Deux emplois en découlent. La fenêtre de quêtes retire ce qu'elle refait
elle-même : le titre, repris dans son bandeau ; le succès, l'étape et la phrase
de départ, que ce bandeau donne en plus court ; et les quêtes précédentes, que
son pied donne en boutons. Il ne reste alors du bandeau d'intro que la
récurrence, et seulement sur les quêtes qui en ont une ; quand il ne reste rien,
c'est le bandeau entier qui part, sans quoi sa seule bordure ouvrirait le guide
sur une bande vide. La fenêtre des pages liées, elle, ne prend que le cadrage,
sans le suivi d'étapes qui n'aurait aucun sens sur une carte, et garde le titre,
qui y est le seul repère.

## D34 - Retenir la place des fenêtres, sur plusieurs écrans

Deux écrans de densités différentes suffisent à rendre fausse toute mesure
naïve. Trois choix en découlent, chacun tranché par la mesure.

**Les coordonnées sont celles du bureau, prises à GetWindowRect.** Le rectangle
« normal » de WINDOWPLACEMENT, qui semblait fait pour cela, est exprimé dans la
densité de l'écran principal : une fenêtre de 780 x 1140 posée sur un second
écran à cent cinquante pour cent revenait à 570 x 761. Les coordonnées WPF ont
le même défaut, aggravé : elles dépendent de l'écran qui porte la fenêtre.

**La place est appliquée deux fois.** Le premier appel déplace la fenêtre, ce
qui la fait changer d'écran donc de densité ; la taille vient d'être posée dans
la densité de départ, et WPF la reproportionne en encaissant le changement.
Mesuré : 800 x 620 revenaient à 533 x 413. Le second appel, différé d'un tour de
boucle de messages, rend la bonne taille. La position, elle, était juste dès le
premier.

**Une place hors de tout écran n'est pas rendue.** Un écran débranché laisserait
la fenêtre ouverte, présente dans la barre des tâches, et invisible. Il faut
qu'un écran en laisse voir au moins cent vingt pixels dans les deux dimensions,
de quoi saisir la barre de titre.

Les outils de développement du dépôt ont dû être recalibrés pour le vérifier :
ils se déclaraient conscients de la densité du système quand l'application l'est
par écran, et mesuraient donc des coordonnées mises à l'échelle. Ils ont montré
un déplacement là où l'application était juste, ce qui a coûté un aller-retour.

## D35 - Ce que le bandeau d'étape promet, et comment il le tient

Le bandeau dit trois choses : à quelle étape on est, ce qu'elle demande, et où
l'on va ensuite. Chacune s'est révélée fausse dans un cas courant, et chacune
a demandé une règle plutôt qu'un correctif.

**Ce qui compte pour une étape.** Le gras seul ne suffit pas. Relevé sur
cinquante-cinq guides, cinq cent trente-deux paragraphes en gras ne donnent que
trois cent cinquante-sept consignes : le reste décrit les sorts d'un boss,
commente un choix de dialogue sans effet, ou titre une liste. Ceux-là se
retrouvaient résumés par leur première phrase, faute d'avoir quoi que ce soit à
résumer.

La marque retenue est grammaticale et non lexicale : l'impératif de la deuxième
personne du pluriel, terminaison en « -ez » hors pronom sujet, plus six
irréguliers. Une liste de verbes avait été essayée d'abord ; elle jetait « Faites
votre lit. », « Consultez la lettre de Mériana. », « Protégez Juzie et
Mériana ! », et il s'en serait trouvé d'autres à chaque guide. Deux pièges au
passage : le découpage en mots doit être unicode, sans quoi « Protégez » se casse
en deux à l'accent, et le seuil de longueur doit laisser passer « Tuez ».

**Le rang.** Le départ porte le rang zéro et son ancrage est le haut du guide ;
le premier paragraphe se trouvant à une cinquantaine de pixels en dessous, il
passait la marque de lecture avant qu'on ait rien fait défiler. Le départ vaut
donc tant que la page n'a pas bougé, et la marque ne gouverne que les
paragraphes.

**Le rang choisi.** Une étape désignée au bouton est retenue jusqu'au prochain
défilement de la main. Sans cela, le défilement qu'on demande traverse les
étapes intermédiaires et les rapporte une à une ; et sur un guide trop court
pour défiler, l'étape visée n'atteint jamais la marque et le bandeau revenait
aussitôt en arrière, le clic passant pour n'avoir rien fait.

**Le résumé.** Le nom capturé est borné par la classe fermée des mots-outils -
conjonctions, prépositions, déterminants, pronoms - et non par une liste tirée
des cas rencontrés, qui s'allongerait à chaque guide. Les particules qui
appartiennent aux noms en sont exclues : « Gardien du Donjon de Belladone » doit
survivre là où « Grand jarl Ordyn et en vous mettant en route » doit être coupé.
Un plafond de six mots ferme la porte au reste.

Les amorces qui annoncent un personnage sont relevées, pas devinées, et ce
qu'on refuse compte autant que ce qu'on accepte : « vous emmène à Astrub » et
« vous êtes à Albuera » introduisent des lieux, et accepter « à » suivi d'une
majuscule ferait parler à une ville.

Une majuscule ne borne rien sans précaution : en .NET, l'indifférence à la casse
s'applique aussi aux catégories Unicode, et « \p{Lu} » accepte alors les
minuscules. Elle est donc rendue sensible à la casse explicitement.

**L'attente.** Une fenêtre native se dessine au-dessus de tout élément WPF du
même châssis : un voile posé sur la vue resterait invisible. La vue est donc
retirée le temps du chargement et l'indicateur prend sa place, comme le fait
déjà la liste déroulante. C'est aussi ce qu'on veut : le guide périmé resté à
l'écran est précisément ce qui trompait.

Toutes les navigations ne sont pas les nôtres, et il faut les distinguer par
leur identifiant. Un lien de quête cliqué dans le guide est refusé, puis relancé
par nos soins : la navigation refusée signale sa fin, et elle le fait après que
la nôtre a commencé. L'attente s'éteignait alors aussitôt, sur le chemin même où
elle servait le plus.

La ligne d'étape, enfin, tient sa place pendant le chargement. Elle disparaissait
faute d'étapes connues, et le bandeau perdait une ligne pour la reprendre une
seconde plus tard. Ce qu'elle montre en attendant est le départ, qui vient des
métadonnées et n'attend pas la page.

## D36 - Les donjons, et pourquoi ils ne se lisent pas comme les quêtes

Le site publie ses donjons dans un format bien plus régulier que ses quêtes, et
ce format commande la façon de les montrer.

**Tout tient en une requête.** Le niveau, la position et le personnage sont dans
les métadonnées ; la clef et la pierre d'âme ne vivent que dans le corps de
l'article. Demander le contenu rendu avec le reste coûte quatre mégaoctets une
fois par indexation, contre quatre-vingt-trois requêtes autrement.

**On lit les classes, jamais les libellés.** « Clef : » est un texte à l'usage
des lecteurs d'écran et peut être réécrit ; « pcd-info__row--key » est du code.
La même règle vaut déjà pour les quêtes.

**Ce qu'on ne prend pas.** Les vignettes de boss et de clefs viennent des
serveurs d'Ankama. Elles s'affichent dans la page, qui est celle du site, mais
rien n'en est extrait : notre liste reste du texte.

**Le classement par palier.** Quatre-vingt-trois lignes ne se parcourent pas
d'un œil, et l'on n'y cherche pas un nom mais ce qui est à sa portée. Les
paliers de cinquante niveaux coupent la liste comme les succès coupent celle des
quêtes. Les trois donjons dont le site ne donne pas le niveau ferment la marche
sous leur propre intertitre : les ranger au niveau zéro les mettrait en tête, ce
qui serait faux.

**Les sections plutôt qu'un résumé.** Une page de donjon n'ordonne rien, elle
expose : les monstres, les salles, le boss, la mécanique, les succès. Ses titres
sont réguliers — « Boss » sur 82 pages, « Liste des salles » sur 81 — là où une
page de quête n'a aucun titre de section, vérifié sur six guides. Les deux
affichages ne se gênent donc pas, et le pont distingue les deux au bloc
d'en-tête, qui est du code du site.

Le départ garde son rang et sa règle : la position et le gardien du donjon
passent par le même composeur que celui des quêtes, sans qu'on ait rien à écrire.

## D37 - Les chemins, les raids et les tanières

**Un raid, une tanière et un donjon sont la même chose** : un lieu qu'on nettoie,
à un niveau donné. Un genre porté par le même type, donc, et non trois types
presque identiques ; ce qui les sépare est la section où on les cherche, pas leur
forme. Une tanière emploie d'ailleurs le bloc structuré des donjons.

**Le niveau se lit à deux endroits.** Les donjons le mettent dans leurs
métadonnées ; les raids et les tanières, sauf une, l'écrivent en clair dans leur
première ligne, « Niveau : 190 ». On lit les métadonnées d'abord, la prose
ensuite, ce qui couvre les dix.

**Les chemins ne forment pas une famille.** Certains mènent à un donjon, les
autres à une île, un zaap ou un souterrain, et servent alors une quête. Ils se
rangent donc dans la branche qu'ils servent, sous une sous-branche à eux : un
chemin ne se compare ni à une zone ni à un donjon, et les mêler allongerait une
liste qu'on parcourt déjà longuement.

Le site ne dit pas de quel côté ils vont : ses catégories ne donnent que la
zone, et les liens de ses pages sont presque toujours absents. Le titre suffit,
à deux conditions : le mot « donjon » écrit en toutes lettres, ou au moins deux
mots distinctifs partagés avec un donjon du catalogue. Un seul mot commun ne
suffit pas, et c'est ce qui écarte les faux — « Zaap du village de la canopée »
ne partage que « canopée » avec « Canopée du Kimbo ». Vérifié sur les vingt et
un chemins publiés, qui sont les cas de test.

**Une page se lit par ses titres quand elle en a.** La règle qui distinguait le
dossier de la consigne s'appuyait sur le bloc des donjons ; elle s'appuie
désormais sur la présence de titres de sections, ce qui couvre du même coup les
tanières et les chemins, dont les titres sont les étapes du trajet. Une page de
quête n'ayant aucun titre de section, rien ne change pour elles.

## D38 - Les icônes de la racine

**La couleur dit le genre, la forme dit lequel.** Un donjon, un raid et une
tanière sont le même genre de lieu et gardent donc la même teinte ; ce qui les
sépare est leur dessin. Leur donner trois couleurs aurait porté la liste à huit,
où six suffisent déjà à peine sur soixante lignes.

**Une clef ne devait plus dire deux choses.** La racine des donjons portait une
clef, et la colonne de droite en porte une pour les soixante-treize donjons qui
en exigent une. Une recherche montrait donc la même clef deux fois sur une
seule ligne, une fois pour dire « donjon » et une fois pour dire « il vous en
faut une ». Mesuré sur « bworker ». Le donjon prend une tour crénelée, la clef
ne veut plus dire qu'une chose.

**Le crâne a été choisi sur mesure, pas sur intention.** Deux épées croisées
avaient été dessinées d'abord pour les raids, puis montées dans l'application et
regardées à la taille réelle, vingt-deux pixels : la garde et le pommeau n'y
portent plus, et il ne reste qu'une croix, à quelques centimètres de celle qui
vide le champ de recherche. Le crâne tient parce qu'il n'est fait que de masses,
une calotte, deux orbites, une mâchoire. La leçon vaut au-delà de ce cas : un
dessin de quinze unités se juge monté, jamais sur son tracé.

**La tanière prend une empreinte** parce qu'elle est l'antre d'une bête, et que
c'est précisément ce qui la sépare du donjon, bâti de main d'homme.

## D39 - Le plan d'une page qui n'a pas de titres

Une page se parcourt par ses titres de second rang. Quatre-vingt-trois donjons
sur quatre-vingt-trois les écrivent là, et huit chemins sur vingt et un ; les
autres chemins n'ont aucun titre et retombent sur les consignes, comme une
quête.

**Deux familles échappaient à cette règle.** Les deux raids n'ont, dans toute
leur page, qu'une seule balise de titre : « Sommaire ». Sept tanières sur huit
descendent les leurs au quatrième rang. Toutes se lisaient donc comme des
guides de quête, et la tanière du Piou n'annonçait qu'une étape.

**À défaut de titres, le sommaire que la page se donne.** C'est une liste de
liens vers ses propres ancres, donc un plan et des points d'arrêt en une seule
fois. Le risque était d'attraper des pages de quête : aucun des sept cent
quatre-vingt-deux guides n'en porte, la règle ne peut pas les atteindre. Elle ne
s'applique d'ailleurs qu'à défaut, si bien que la tanière Arakne, seule des huit
à écrire ses titres au second rang, se lit comme un donjon.

**Le site se trompe d'ancre trois fois sur quarante** : « #salles » pour
« salle », « #succès » pour « stratégies ». Le texte du lien la retrouve, réduit
à ce qui l'identifie, sans accents ni article, et comparé aux identifiants de la
page. Trente-sept liens sur quarante trouvent ainsi leur cible ; les trois
autres n'en ont aucune sur la page et leur entrée est passée.

## D40 - Ce qu'on montre pendant qu'une page charge

**Le rond d'attente se pose en haut.** Il était centré dans la vue, qui fait
mille trois cents pixels de haut : vingt-deux pixels de rond tombaient à cinq
cents pixels sous le bandeau, loin de l'œil, au milieu d'une étendue vide qui se
lit comme une panne. Il se pose là où le texte va paraître.

**La ligne d'étape dit ce qu'elle attend.** Elle reste affichée pendant le
chargement pour que le bandeau ne saute pas d'une hauteur, mais elle n'y
montrait que deux flèches éteintes, sans numéro ni texte, ce qui se lisait comme
une page cassée.

**Le titre n'est plus répété sous le rond.** Il y avait été mis pour dire que le
clic avait été entendu ; le bandeau le porte désormais dès le début du
chargement, deux lignes plus haut.

## D41 - Un nombre entre parenthèses doit dire ce qu'il compte

Un donjon s'écrivait « Bworker (180) » et une zone « Astrub (37) » : le même
signe pour un niveau et pour un compte de quêtes. Le niveau se dit maintenant
« niv. 180 ». La forme abrégée, et non « niveau », parce que la liste en compte
quatre-vingt-treize lignes.

## D42 - Une zone se range par ses prérequis

**Le rejet en fin de liste était faux deux fois.** Les quêtes qu'aucun succès ne
réclame, deux cent quatre-vingt-quatre sur sept cent quatre-vingt-deux,
partaient toutes sous un intertitre « Hors succès », par ordre alphabétique.
Faux de place : beaucoup ouvrent un succès ou le prolongent, et les voir en bas,
coupées de ce qu'elles servent, ne dit rien de la progression. Faux d'ordre :
les quatre-vingts quêtes d'alignement bontarien forment une suite numérotée que
l'alphabet lisait « 1, 10, 11, 12, 2 ».

**Les prérequis suffisent à ranger la zone.** Cinq cent quatorze prérequis sur
sept cent vingt-neuf désignent une quête du catalogue, et cent
quatre-vingt-douze des deux cent quatre-vingt-quatre quêtes seules sont prises
dans une chaîne. Un tri topologique sur ces liens rend la progression du site.

**Un succès reste un bloc insécable**, et c'est ce qui crée les seules boucles :
deux succès qui se réclament l'un l'autre par des quêtes différentes ne peuvent
pas être départagés. Au Château d'Amakna, le succès « Étre plus royaliste que le
roi » ouvre deux suites de quêtes seules qui reviennent toutes deux en prérequis
de ses propres quêtes. On tranche alors par l'ordre d'avant, et l'ordre reste
total. Mesuré sur toutes les zones : **huit rangs forcés**, dont six sur la seule
île de Frigost.

**Une quête que rien ne lie ne se range pas, elle attend en fin de liste.**
Laissée dans le tri, elle en sortait au hasard : au Château d'Amakna, « On
recherche Ali Grothor » se glissait entre deux succès parce qu'elle était la
seule chose que le tri pouvait sortir pendant qu'une boucle bloquait le second.
Le site ne dit rien de sa place ; elle va donc où allaient toutes les quêtes
seules avant ce rangement, à la fin. Cent deux quêtes seules sur trois cent une.
Un succès sans lien, lui, garde son rang : celui-là, le site le donne.

**À défaut de prérequis, rien ne bouge.** Le départage est exactement l'ordre
d'avant, rang du succès sur le site puis nom, une quête seule passant après les
succès de même rang. Les vingt quêtes seules d'Astrub, dont aucune ne nomme une
autre quête, restent donc où elles étaient.

**Le calcul est dans le noyau** (`QuestZonePlan`), et non dans la vue : c'est la
seule couche que les tests atteignent, le projet de tests visant `net10.0` quand
l'application vise `net10.0-windows`. Douze tests y couvrent les cas mesurés.

## D43 - Le retrait dit l'appartenance

**L'intertitre et ses quêtes étaient au même retrait.** La liste se lisait donc
comme une suite plate d'où émergeaient des étoiles, et rien ne montrait qu'un
succès contenait les lignes suivantes. Un anneau posé sur les quêtes seules
avait d'abord été essayé : il ne dit rien de lui-même, s'apprend, et un cercle
vide dans un suivi de quêtes se lit surtout comme une case à cocher.

**Les quêtes d'un succès se décalent et se relient par un filet vertical.** Une
quête au ras de la marge n'appartient alors à aucun succès, et cela se voit sans
légende. Le filet montre du même coup où un succès commence et où il finit, ce
que six lignes de suite ne disaient pas.

**Le filet se hachait d'un rang à l'autre.** Un tracé qui déborde de la marge
d'une ligne n'était pas dessiné : mesuré, quarante-cinq pixels de filet pour six
de vide. La cause est le remplissage que le conteneur de ligne se donne ; il est
mis à zéro, et le débordement passe alors. La liste y gagne trois pixels de
hauteur par ligne, et toutes les lignes la même hauteur, ce qui n'était pas le
cas.

## D44 - Le panneau des prérequis se ferme par où il s'ouvre

**À gauche du cadenas et non dessous.** Posé dessous, il recouvrait les lignes
suivantes, et le décalage de deux cent soixante pixels qui le ramenait dans la
fenêtre était une mesure prise une fois, que la largeur du panneau dément dès
qu'un prérequis est long. À gauche, il se cale seul sur le cadenas, quelle que
soit sa taille.

**La fenêtre le referme, il ne se ferme plus tout seul.** Laissé à lui-même, il
se fermait au clic sur le cadenas qui l'avait ouvert, puis ce même clic le
rouvrait aussitôt : on ne pouvait pas le refermer par où on l'avait ouvert.
Sa fermeture ne prévenait d'ailleurs personne, l'événement ne se déclenchant
pas, mesuré à la trace, ce qui ôtait tout moyen de la rattraper. La fenêtre
ferme donc au premier clic hors du cadenas propriétaire, et le cadenas bascule.

## D45 - Les guides comptent comme le panneau

L'application s'arrête quand il ne reste plus rien à l'écran : ni fenêtre de
jeu, ni panneau de réglages. La règle existe parce qu'une application invisible
serait injoignable, les raccourcis ne répondant que lorsqu'une de nos fenêtres a
le premier plan.

Les guides n'entraient pas dans ce compte, alors qu'ils remplissent les deux
conditions : ils sont à l'écran, et ils reçoivent les raccourcis, ce qui était
déjà admis ailleurs. Fermer la dernière fenêtre de jeu, ou masquer les réglages,
emportait donc le guide qu'on était en train de lire, alors que c'est
précisément fenêtres de jeu fermées qu'on prépare une session.

Ils tiennent maintenant l'application en vie, et les masquer alors qu'il ne
reste rien d'autre l'arrête, exactement comme masquer le panneau. Vérifié à
l'écran : guides seuls affichés, réglages masqués, l'application survit ;
guides masqués à leur tour, elle s'arrête.

## D46 - La mise à jour depuis le dépôt

**Un dépôt public, sans jeton.** L'application demande la dernière livraison à
l'API du dépôt, soixante fois par heure et par adresse, ce qui dépasse de loin
une vérification par démarrage. Un dépôt privé aurait demandé un jeton, et un
jeton posé dans l'exécutable est lisible par qui l'ouvre : ce n'est pas un
secret, c'est un secret publié.

**L'empreinte avant l'exécution.** Un exécutable de soixante mégaoctets qui
remplace le nôtre ne s'exécute pas sur la foi d'un téléchargement. La livraison
porte un second fichier, `DtHub.exe.sha256`, et une livraison à laquelle il
manque est ignorée. Un écart, et le fichier est effacé sans avoir servi.

**L'échange à l'arrêt, jamais en session.** Un exécutable qui tourne ne peut pas
être écrasé, mais il peut être renommé : l'ancien s'écarte en
`DtHub.exe.ancien`, le nouveau prend sa place, et le démarrage suivant balaie ce
qui reste. Si la seconde moitié échoue, la première est défaite : mieux vaut
l'ancienne version que pas d'application. Rien ne demande de droits
particuliers, la mise à jour se posant là où l'application est déjà installée.

**La note de version attend le démarrage suivant.** C'est celui qui exécute la
nouvelle version, et c'est donc là qu'annoncer ce qui change a un sens. Elle est
écrite à côté de l'exécutable en attente, lue une fois, puis effacée. Avant
cela, un bandeau du panneau dit qu'une version est prête et donne à lire sa
note.

**Pas de mise à jour depuis un arbre de sources.** Si le fichier de solution se
trouve au-dessus de l'exécutable, la mise à jour est refusée : le lanceur de
développement republie à chaque démarrage et l'écraserait dans la seconde, en
faisant croire à une régression.

**Rien de tout cela n'est une panne.** Pas de réseau, dépôt encore absent, quota
atteint, empreinte fausse, fichier verrouillé : l'application continue avec la
version qu'elle a. C'est un service de confort, pas une dépendance.

**Le service est dans l'infrastructure et non dans la vue.** Il ne touche que
des fichiers, et c'est la seule couche que les tests atteignent. Ce qu'il fait
étant de remplacer un exécutable, il valait mieux l'éprouver : sept tests
couvrent la pose, le refus sur empreinte fausse, le refus en arbre de sources,
la note lue une seule fois et le ménage.

**Aucune action tierce dans la chaîne de livraison**, seulement celles de GitHub
et son outil en ligne de commande, déjà présent sur la machine de compilation.
Une chaîne qui pose l'exécutable que des gens vont exécuter n'emprunte pas de
code à des inconnus.
\n

## D47 - Une fin de session Windows vaut un « Quitter »

L'état de la session, la place des fenêtres et celles qui étaient ouvertes,
n'était retenu qu'au « Quitter » et quand il ne restait plus rien à l'écran.
Un arrêt ou un redémarrage du poste passe par un troisième chemin, que rien
n'écoutait : les fenêtres revenaient alors à leur place de l'avant-dernière
fois, celle du dernier arrêt volontaire. Mesuré sur une fenêtre déplacée en
1700,250 : le fichier gardait 420,120.

**Ce qui se règle, lui, ne craint rien.** Raccourcis, qualité, distance, taille
des fenêtres, appareils, mise à jour automatique : tout cela s'écrit à l'instant
où on le change. Vérifié en coupant le processus d'autorité, la case décochée
avait survécu.

**L'écriture est attendue, non lancée en fond.** Windows n'accorde que quelques
secondes avant de fermer d'autorité, et une écriture lancée sans être attendue
n'a aucune chance d'arriver. Elle est attendue en laissant tourner la boucle de
messages, faute de quoi les suites qui reviennent sur le fil d'affichage
attendraient un fil qu'on aurait soi-même bloqué. Une minuterie borne l'attente
à trois secondes : mieux vaut un état à moitié écrit qu'une session que l'on
retient.

**Une coupure brutale reste une coupure brutale.** Panne de courant, arrêt forcé
du processus : la place des fenêtres est perdue, et l'application rouvre sur la
dernière connue. Il faudrait écrire à chaque déplacement pour y remédier, ce qui
coûterait une écriture par pixel parcouru.
\n

## D48 - Demander au site plutôt que compter les jours

Le catalogue se relisait au bout de sept jours, que le site ait bougé ou non.
Une quête parue le matin pouvait donc attendre une semaine, et une semaine sans
publication coûtait quand même une relecture de dix-huit mégaoctets.

**Une demande de quatre-vingt-dix-sept octets dit tout.** L'API du site rend la
date du dernier article modifié, et le compte total dans un en-tête. Les deux
retenus, on sait si quelque chose a paru, disparu ou été corrigé. Le rapport est
de un à deux cent mille.

**Le délai reste en filet.** Si le site cesse de répondre à cette demande-là, le
catalogue vieillit quand même et finit par être relu. Et un site qui ne répond
pas ne provoque jamais de relecture : garder ce qu'on a vaut mieux que jeter un
catalogue faute de réseau.

**Une heure de patience.** Ouvrir et refermer la fenêtre dix fois dans l'heure ne
doit pas produire dix demandes, si petites soient-elles.

## D49 - Une sonde qui dit ce qui ne se lit plus

L'application fait des suppositions sur la forme des pages du site, et quand
elles cessent d'être vraies rien ne le signale : elle affiche simplement moins
bien. Les tanières n'annonçaient qu'une étape et les raids aucune ; ces deux
défauts ont vécu des semaines et ont été trouvés à l'œil, par hasard.

**Elle vérifie des suppositions, pas une implémentation.** Que chaque donjon
porte un titre de second rang, que chaque raid et chaque tanière porte un
sommaire, qu'aucun guide de quête n'en porte, que les consignes soient mises en
évidence. Ce sont les faits dont le code dépend, énoncés séparément de lui : les
vérifier avec le code qu'ils justifient ne prouverait rien.

**Ce qui se compte se compare.** Une quinzaine de nombres sont relevés et
confrontés à `build/sonde-papycha/reference.json`. Une baisse est un signal, le
site supprimant rarement quand l'application cesse de lire souvent ; une hausse
est la vie normale du site. Le relevé se rebénit à la main quand l'écart est
légitime.

**Hors des tests, dans la solution.** La règle du dépôt interdit au projet de
tests d'avoir besoin du réseau. La sonde vivait déjà dans `build/`, hors de la
solution et donc jamais compilée : elle y est entrée, ce qui la tient au moins
compilable. Son premier passage a d'ailleurs mesuré deux faits qu'on ignorait,
un donjon sans bloc d'en-tête sur quatre-vingt-trois, et six liens de sommaire
sur quarante qui pointent une ancre absente.
\n

## D50 - Un fichier qu'on donne, et qui ne laisse rien

**Ni installateur, ni auto-installation.** Un installateur devrait être signé
sous peine d'un avertissement à chaque version, et poserait l'application là où
elle ne peut plus se remplacer sans droits administrateur, ce qui tuerait la mise
à jour. Quant à s'installer elle-même : se copier laisse un exécutable orphelin
qui ne se mettra jamais à jour, et qu'on relancera un jour par habitude sans
savoir qu'il est vieux ; se déplacer revient à bouger le fichier de quelqu'un
sans le lui demander, et casse sur une clef USB. Ce qui manquait au fichier seul
n'était pas d'être installé, c'était d'être retrouvable.

**Un raccourci, récrit à chaque démarrage.** Il vise l'exécutable là où il est.
Déplacer le fichier suffit à le corriger, sans rien demander. Rien n'est fait
depuis un arbre de sources, le raccourci viserait une sortie de publication que
le lanceur de développement récrit à chaque fois : c'est la règle déjà écrite
pour la mise à jour, réutilisée telle quelle.

Il se pose par l'interface COM du shell, première du dépôt : le script
PowerShell qui fait la même chose ne peut pas servir, PowerShell étant proscrit à
l'exécution.

**Le cache du moteur de rendu déménage.** Sans adresse, il se posait à côté de
l'exécutable : vingt-quatre mégaoctets après une session sur un dossier vierge,
trois cent quatre-vingt-dix-neuf après quelques semaines, dont trois cent
trente-huit de seul cache web. C'est le comportement normal de Chromium, qui
dimensionne son cache sur la place libre du disque, et c'est démesuré pour une
application qui montre des guides. Il écrit désormais dans le dossier de
l'utilisateur, avec le reste, et son cache est borné à cent mégaoctets.

La borne passe par un commutateur de ligne de commande, dont la documentation
prévient que certains sont ignorés. Un balayage au démarrage, au-delà de deux
cents mégaoctets, est le filet : mesuré à trente et un mégaoctets après cinq
guides, la borne n'a pas encore été mise à l'épreuve.

**Le composant WebView2 se vérifie au seul endroit qui les couvre tous les
deux.** Les deux fenêtres qui en portent un passent par le même environnement :
c'est là que son absence se constate, et le message remonte par l'exception que
la fenêtre affiche déjà à la place de la page.
\n

## D51 - Nous entrons dans des mises en page que le site n'éprouve pas

La carte d'un donjon recouvrait l'en-tête, et avec lui le niveau et la pierre
d'âme, dès que la fenêtre s'élargissait. Le réflexe serait d'accuser notre
feuille de style ; la mesure dit autre chose.

**Le site fait cela chez lui aussi, mais ne s'y expose jamais.** Sa carte porte
une marge haute négative pour se glisser à côté de l'en-tête : moins dix-huit
pixels en une colonne, moins quatre-vingt-quatorze en deux. Il passe à deux
colonnes au-delà de huit cent quatre-vingts pixels de contenu. Or sa colonne de
guide en fait six cent cinquante, un volet latéral prenant le reste : la branche
à deux colonnes ne s'active jamais chez lui. Nous écartons ce volet, la colonne
prend toute la place, et nous y tombons les premiers. Vérifié sur deux donjons,
de neuf cent vingt-deux à mille neuf cents pixels : recouvrement partout.

**C'est notre fenêtre, donc c'est notre correctif.** Une règle annule la marge :
la carte descend à sa place, à droite des faits, et plus rien ne se recouvre.

**Les autres pages sont saines.** Quête, raid, tanière, chemin, page de rubrique
ont été passées à la même sonde à la largeur de la fenêtre : aucune marge
négative, aucun débordement, aucun chevauchement. Seuls les donjons portent ce
bloc.

**La sonde reste**, dans `build/sonde-papycha/mise-en-page.js`. Le jour où le
site changera de mise en page, c'est elle qui le dira, plutôt qu'une capture
d'écran envoyée après coup.
\n

## D52 - La sentinelle regarde ce qu'elle lit, et rien d'autre

Une empreinte pour tout le site déclenchait une relecture dès qu'un seul de ses
mille douze articles bougeait. Or on n'en lit que neuf cents, rangés dans cinq
catégories : une correction d'orthographe sur une page qu'on ignore coûtait
cinquante secondes à chaque utilisateur.

Les empreintes sont donc prises catégorie par catégorie. Cinq demandes de
quarante octets, deux cents en tout, contre quatre-vingt-dix-sept pour l'ancienne
demande unique : le prix triple et le déclenchement devient juste.

Le relevé du jour dit pourquoi cela vaut la peine : les quêtes ont bougé le
1er septembre, les chemins le 29 août, les donjons le 30, les raids le 28, les
tanières le 18. Cinq rythmes distincts, qu'une seule date écrasait.

**Une catégorie muette annule tout le relevé.** Retenir la moitié des empreintes
ferait croire au repos sur les autres, et l'on cesserait de relire une catégorie
qui a changé. Mieux vaut ne rien retenir et s'en remettre au délai de sept jours,
qui reste en filet.

**Ce qui reste à faire** : la relecture est encore complète quand une seule
catégorie bouge. Les donjons pèsent quatre mégaoctets et quatre cinquièmes du
temps ; les relire quand seule une quête a changé reste du gâchis. Le filtre
`modified_after` de l'API est vérifié et permettra de ne reprendre que les pages
touchées.
\n

## D53 - Un bouton qui double un automatisme n'a pas lieu d'être

Le pied de la liste portait la date de dernière lecture et un lien « Relire ».
Les deux sont retirés.

**Le bouton ne servait qu'à devancer un quart d'heure.** La sentinelle demande au
site, pour deux cents octets, s'il a bougé, et relit alors d'elle-même. Le
bouton ne gagnait que le délai de politesse qui empêche d'interroger le site à
chaque ouverture de fenêtre. Ce délai est passé d'une heure à un quart d'heure,
ce qui coûte deux cents octets de plus par heure d'usage et remplace le bouton.

**La date ne servait à personne.** Savoir que les guides ont été lus il y a six
minutes n'appelle aucune décision, puisqu'il n'y a plus rien à décider.

**Ce qui reste est le seul fait utile** : quand une relecture rapporte quelque
chose, elle le dit. Et seulement alors : le site remanie souvent ses pages sans
que le catalogue en gagne ou en perde, et l'annoncer à chaque fois serait du
bruit.

**La cadence du site, mesurée, contredit l'intuition.** Articles modifiés en sept
jours : quarante quêtes sur sept cent quatre-vingt-deux, trente-quatre donjons
sur quatre-vingt-trois, dix chemins sur vingt et un, un raid, aucune tanière.
Sur trente jours, sept cent soixante-dix-huit quêtes sur sept cent
quatre-vingt-deux, ce qui trahit une réécriture d'ensemble plutôt qu'un travail
éditorial. Le site n'est donc pas calme, mais ses remaniements ne changent
presque jamais ce que le catalogue retient.

**Ce qui a été écarté pour cette raison** : la relecture par différence, qui ne
reprendrait que les pages touchées. Elle demanderait de fusionner un catalogue
partiel dans l'ancien, donc un moteur de fusion et ses tests, pour épargner une
lecture de cinquante secondes qui se fait en fond, fenêtre utilisable. Le
rapport n'y est pas.
\n

## D54 - Le pont n'est pas une conversation avec nous-mêmes

Le script du pont est posé par `AddScriptToExecuteOnDocumentCreatedAsync`, donc
sur *tout* document que la fenêtre charge, et `postMessage` est ouvert à toute
page. Ce qui en revient est une entrée, pas une réponse.

**Mesuré**, en rejouant l'ancienne lecture sur ce qu'une page peut poster :

| Message posté | Ce que faisait l'ancienne lecture |
|---|---|
| `{}` | `KeyNotFoundException` |
| `[]`, `null`, `42`, `"loaded"` | `InvalidOperationException` |
| `{"genre":"loaded"}` | `KeyNotFoundException` |
| `{"kind":"step"}` | `KeyNotFoundException` |
| `{"kind":"step","index":"4"}` | `InvalidOperationException` |
| `{"kind":"loaded","steps":[1,2]}` | `InvalidOperationException` |

Neuf formes sur neuf. Seule `JsonException` était rattrapée, et aucune de ces
neuf n'en est une. Une exception dans un gestionnaire d'événement WPF n'est
rattrapée par personne : l'application tombe. À quoi s'ajoute
`TryGetWebMessageAsString`, qui lève `ArgumentException` quand le message n'est
pas du texte, avant même qu'on ait lu quoi que ce soit ; c'est la documentation
du composant qui le dit, pas une supposition.

La lecture descend donc dans le noyau, en fonction pure, avec ses vingt-cinq
cas. Elle rend un message ou rien, jamais une exception. Le sonde est conservée
dans `build/sonde-pont` : c'est elle qui a produit le tableau.

## D55 - Nos fenêtres ne chargent que le site

Deux fenêtres affichent le web sans barre d'adresse, sous notre titre et notre
icône. Ce qu'elles chargent doit donc être ce que nous avons promis d'afficher.

Ce n'était pas le cas. La fenêtre des pages liées naviguait vers n'importe
quelle adresse qu'un lien lui tendait, sans filtre de schéma ni d'hôte, et
n'écoutait ni la navigation ni les ouvertures en fenêtre neuve : un
`target="_blank"` y ouvrait une fenêtre du moteur, hors de tout contrôle.

La règle est une fonction du noyau, `PapychaSite.Owns` : schéma sûr, et l'hôte
du site ou l'un de ses sous-domaines. **La comparaison porte sur l'hôte que rend
l'analyseur d'adresses, non sur le début du texte** : `https://papycha.fr@ailleurs.example/`
commence par le nom du site sans lui appartenir. Les sous-domaines sont admis
parce que `www` redirige vers le nom nu, et qu'une redirection est annoncée
comme une navigation avant d'être suivie.

Le clair est refusé : les neuf cent dix-huit adresses du catalogue sont en
`https`. Ce qui sort du site part au navigateur par `OpenUrl`, qui n'ouvre lui
aussi que du `https` - donc ni `file://`, ni `javascript:`, ni un URI que le
shell interpréterait.

Le parseur de rubriques, qui posait déjà la même question par un préfixe de
texte, lit maintenant la même règle.
\n

## D56 - Suivre les adresses, pas les noms

Le site nomme un succès à deux endroits : l'intertitre d'une page de rubrique et
le bloc d'intro de chaque quête. Il ne l'écrit pas pareil.

| L'intertitre écrit | La quête écrit |
|---|---|
| Brûler le pissenlit **à** la racine | Brûler le pissenlit **par** la racine |
| Fri **C**arré | Fri **c**arré |
| **E**tre plus royaliste que le roi | **É**tre plus royaliste que le roi |
| L'heure c'est l'heure | L'heure**,** c'est l'heure |
| Glo**b**litération | Goblitération |
| Plus dure sera la t**ê**te | Plus dure sera la t**â**te |

C'est le nom porté par la quête qui fait foi partout ailleurs : c'est lui qui
groupe les quêtes en succès. Le rang, lui, se prenait sur l'intitulé.

**Mesuré sur le vrai site, par le code livré** : des quatre-vingt-seize entrées
de la liste des rangs, trente ne désignaient aucun succès du catalogue, et
quarante-neuf succès sur cent quinze se retrouvaient sans rang, donc rejetés en
fin de zone.

La correction ne compare plus les noms : elle suit les adresses. Un intertitre
coiffe des quêtes, ces quêtes portent un nom de succès, et c'est ce nom-là qui
prend le rang. Ni normalisation ni rapprochement approximatif, donc rien à
régler et aucun faux rapprochement possible. **Quatre-vingt-dix-sept rangs,
aucun nom en trop, dix-huit succès sans rang.** Les rangés par ordre
alphabétique passent de trente-huit à douze.

Tous les intertitres en gras comptent désormais, et non les seuls marqués
« [Succès] » : trois succès n'ont pas d'autre intertitre que leur nom nu. Cela
n'inverse aucun rang que le site marque, vérifié.

**Ce qui a été écarté, mesuré aussi** :

- *L'ordre du document*, qui range un succès à la première de ses quêtes citée
  quelque part. Il classerait huit succès de plus, mais **cinquante-trois
  inversions** de l'ordre que les intertitres marquent : un succès de Frigost
  est cité en passant sur la page d'Amakna, et s'y retrouverait rangé. Il
  placerait « Une impression blizzard » après « Agriculture et Alchimie ».
- *Le niveau conseillé*, pour départager les douze qui restent : sept des
  trente-neuf blocs concernés en portent un, et jamais tous ceux d'une même
  zone. Mêler un niveau connu à des inconnus donne un ordre qui a l'air décidé
  sans l'être.
- *La page « Succès » du site*, qui les liste tous : par ordre alphabétique. Elle
  ne porte aucun ordre de jeu, seulement l'orthographe officielle et le nombre
  de quêtes.

La sonde qui a produit ces chiffres est dans `build/sonde-rang`. Elle passe par
le service, donc par le code livré, et non par une réimplémentation qui
pourrait se tromper d'accord avec elle-même.
\n

## D57 - Ce qui se vérifie descend dans le noyau

Le projet de tests ne référence pas `DtHub.App`, par construction : une vue-modèle
WPF traîne un fil d'interface et des dépendances graphiques dont un test n'a que
faire. La conséquence est que toute règle écrite dans une vue-modèle n'est
couverte par rien.

Trois règles en sont sorties, toutes pures, toutes lues par l'utilisateur :

- **La lecture du pont**, `QuestBridgeMessage` : la seule des trois qui portait
  un défaut, et qui tenait l'application (voir [D54](#d54---le-pont-nest-pas-une-conversation-avec-nous-mêmes)).
- **La plage de niveaux d'une zone**, `QuestLevelRange` : elle décide de se
  taire quand trop peu de quêtes portent un niveau, et de dire sur combien
  quand la plage est partielle. Deux seuils, aucun n'était éprouvé.
- **Ce qu'une relecture a rapporté**, `QuestTally` : ses accords, son silence
  quand rien n'a bougé, son silence à la première lecture.

Ce qui reste dans la vue-modèle et pourrait descendre encore : la construction
des lignes de la liste, qui tient à des types d'affichage, et le rapprochement
d'adresses de `TryFollowUrl`, qui emploie une normalisation légèrement
différente de celle du catalogue - deux notions de « même adresse » dans le même
dépôt, ce qui mériterait d'être unifié avant d'être testé.
\n

## D58 - Un téléphone attaché deux fois

ADB 37 rejoint un téléphone tout seul par mDNS alors qu'il est déjà connecté par
son adresse. Le même appareil occupe alors deux transports :

```
192.168.1.14:40187                          device  transport_id:5
adb-XXXXXXXX-XXXXXX._adb-tls-connect._tcp   device  transport_id:6
```

Le commentaire de `MdnsDeviceName` posait l'hypothèse inverse : l'appareil
paraît sous son adresse quand il est joignable, et sous ce nom quand il ne l'est
pas. **Il paraît sous les deux**, et le nom est un mauvais destinataire :
vingt refus en quatre jours dans les journaux, « device 'adb-...' not found »
sur `getprop`, sur `pm list users`, sur la résolution d'activité. Toute commande
sans destinataire échoue par ailleurs en « more than one device ».

Le départage était laissé au hasard. `Deduplicate` groupait par identité, triait
sur « connecté » puis « USB », et prenait le premier : entre deux transports
sans fil également connectés, c'est l'ordre d'ADB qui décidait, donc le numéro
de transport, donc la reconnexion la plus récente.

Deux corrections. `AdbTransportChoice.WithoutDoubles` écarte le nom mDNS **avant
toute interrogation**, quand une adresse joignable désigne le même téléphone
dans le même relevé ; et le départage final préfère explicitement une adresse à
un nom. Le nom garde son emploi quand il est seul : il identifie l'appareil et
sert à s'y connecter. Un appareil que le registre ne connaît pas garde ses deux
lignes, faute de pouvoir affirmer que c'est le même.

**Vérifié sur le vrai téléphone**, les deux transports attachés : une seule
ligne d'appareil dans le panneau, deux instances, aucun refus d'ADB au
démarrage. Huit tests.

## D59 - L'arrêt fait bien son travail, mesuré

`OnExit` est appelé par WPF depuis son propre arrêt, et le répartiteur meurt dès
que la méthode rend la main. Un `await` la lui rendrait au premier travail qui
ne se termine pas sur place. D'où la boucle de messages pompée sous
`DispatcherFrame`, bornée à huit secondes.

Le commentaire redoutait que la borne ne tombe : la sonde d'alors n'avait mesuré
que le cas sans fenêtre de jeu, où la fermeture rend la main d'un trait, et
concluait qu'avec des fenêtres ouvertes « ce qui suit ne se ferait plus. Ce qui
suit, c'est la pose de la mise à jour ».

**Mesuré, deux comptes ouverts sur un vrai téléphone :**

| Étape | Durée |
|:--|--:|
| Fermeture des deux fenêtres de jeu | 626 ms |
| Arrêt entier, pose de la mise à jour comprise | 634 ms |
| Disparition du processus, vue du dehors | 1,2 s |

Sept secondes et demie de marge sur huit. La crainte n'était pas fondée, et les
deux processus scrcpy sont bien partis avec.

Les deux durées sont désormais journalisées à chaque arrêt. C'est le genre de
question qui revient, et elle ne se reposera plus à l'aveugle.
\n

## D60 - Ne pas annoncer une réussite qu'on n'a pas obtenue

`WirelessPairingResult.Paired` se définit comme « tout sauf `PairingFailed` ».
C'est juste : le téléphone a bien accepté le code. Ce n'est pas pour autant de
quoi jouer.

La fenêtre d'association en faisait pourtant son critère. Sur
`ConnectPortNotFound` comme sur `ConnectFailed`, elle affichait **« Téléphone
associé. Il se connectera tout seul, maintenant et à chaque lancement »**, elle
jetait le message qui disait ce qui n'allait pas, et elle se fermait au bout
d'une seconde. Le téléphone n'était pas connecté.

Pire : le message jeté demandait *« Saisissez le port affiché sous Débogage sans
fil »*, et **aucun champ ne permettait de le saisir**. La méthode qui l'aurait
consommé, `DevicePairingService.ConnectAsync(host, port)`, n'avait aucun
appelant. Un utilisateur dont le réseau bloque le mDNS était donc dans une
impasse : on lui disait que c'était réussi, puis on lui demandait une chose
impossible.

Trois corrections, une par maillon :

- La fenêtre ne se ferme et n'annonce la réussite que sur `Connected`. Sinon
  elle montre le message tel quel.
- `NeedsPort` dit ce qui manque, et le champ n'apparaît que dans ce cas : rien à
  refaire, rien qu'un nombre à lire sur le téléphone.
- L'hôte n'est pas redemandé. C'est celui du téléphone qu'on vient d'appairer,
  et le redemander serait faire chercher à l'utilisateur une adresse que nous
  avons déjà.

**Non vérifié sur matériel** : provoquer le cas demanderait de rompre
l'association et de la refaire, donc de lire un code sur l'écran du téléphone.
Le correctif est éprouvé au niveau du noyau, et la fenêtre a été ouverte pour
vérifier qu'elle s'affiche sans erreur de liaison, champ masqué comme il se
doit. Le reste attend le téléphone en main.

## D61 - Les douze raccourcis, éprouvés au clavier

`Ctrl+Tab` et `Ctrl+P` « en conditions réelles » étaient marqués à faire depuis
le début, et `Ctrl+0` avait un jour refusé de répondre sans qu'on sache si
c'était le raccourci ou la simulation qui fautait.

Réponse : la simulation. **Les douze fonctionnent**, éprouvés par de vraies
frappes au niveau du système, deux comptes ouverts sur un vrai téléphone.

| Raccourci | Effet observé |
|:--|:--|
| `Ctrl+P` | Le panneau se masque, puis se réaffiche |
| `Ctrl+Q` | Les guides se masquent |
| `Ctrl+Tab` | XSpace vers Principal |
| `Ctrl+Shift+Tab` | Principal vers XSpace |
| `Ctrl+R` | Les deux fenêtres au même rectangle, au pixel |
| `Ctrl+T` | 0,482 et 1920,482, deux moitiés de l'écran |
| `Ctrl+1` à `Ctrl+4` | 1407x835, 2110x1230, 2813x1626, 3516x2021 |
| `Ctrl+5` | 0,0 en 3840x2160, l'écran entier |
| `Ctrl+0` | Quitte, deux fenêtres fermées en 763 ms |

**Et la portée tient.** Depuis la fenêtre des guides, `Ctrl+T` range bien les
fenêtres de jeu : c'est exactement la situation qui levait deux cent
soixante-quatre fois avant le 1er septembre. Depuis le Bloc-notes, le même
`Ctrl+T` ne bouge rien.

**Une observation non reproduite** : une fois, `Ctrl+3` a rendu une fenêtre de
3840x2186 posée en 0,-58, donc plus haute que l'écran et barre de titre
au-dessus du bord. La géométrie de départ venait d'un rectangle mémorisé
antérieur aux tailles préréglées. Sept tentatives depuis, sur des états connus,
rendent 2813x1626 en 0,234, stable. Consigné sans être qualifié de défaut, faute
de savoir le reproduire.

**Deux outils de développement en sont nés**, et l'antivirus a dicté leur
forme. Un premier script réunissait l'envoi de frappes et l'énumération des
fenêtres : Defender l'a refusé, à raison, c'est la signature d'un journaliseur
de frappes. Rien n'a été désactivé, aucune exclusion n'a été créée. Les deux
tâches sont simplement séparées : `build/frappe.ps1` n'envoie que des frappes,
et `build/premier-plan.ps1` demande le premier plan à l'automatisation
d'interface plutôt qu'à `GetForegroundWindow`.

## D62 - Deux migrations qui ne tiraient jamais, dont une qui mentait

Les paliers huit et neuf du fichier de réglages devaient fondre la qualité
« Haute » dans la maximale et le zoom « très proche » dans « proche ». Toutes
deux gardées par `Enum.IsDefined`.

**Elles ne tiraient jamais.** `TolerantEnumConverterFactory` est installé dans
`JsonDocumentStore`, et remplace toute valeur inconnue par le repli déclaré sur
l'énumération, au moment de la lecture. Quand `Migrate` s'exécute, la valeur est
donc déjà définie, et `Enum.IsDefined` toujours vrai.

Sans conséquence pour la qualité, dont le repli et la cible coïncident. Avec
conséquence pour le zoom : le repli disait « normal » quand la migration visait
« proche ». Un fichier portant `Closest` retombait au réglage d'origine.

Prouvé avant d'être corrigé, par un test sur le fichier qui a réellement coûté
la configuration le 30 août : `Expected: Close, Actual: Normal`.

Le repli porte désormais la décision, puisque c'est lui qui décide, et les deux
migrations mortes sont retirées plutôt que laissées à faire croire qu'elles
agissent. Un commentaire dit à leur place pourquoi il n'y a rien à faire là.

**Vérifié sur le vrai fichier**, par `build/sonde-reglages` :

| | Avant | Après |
|:--|:--|:--|
| Schéma | 8 | 9 |
| Zoom | `Closest` | `Close` |
| Qualité | `Maximum` | `Maximum` |
| Instances | 2 | 2 |
| Raccourcis | 10 | 10 |
| Quarantaine | | aucune |

La sonde existe parce que ce fichier ne peut pas entrer au dépôt : il porte des
identifiants d'appareil, et rien de tout cela n'a à figurer dans un test. Elle
travaille sur une copie, l'original n'étant jamais touché.

