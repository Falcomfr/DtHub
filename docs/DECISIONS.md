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

**2026-08-29 - Acceptée, à rouvrir. Rouverte le 2026-09-02, voir D67.**

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

Deux écritures, et deux seulement : lancer une instance l'y met, le bouton
« Fermer » l'en retire. Quitter l'application, fermer une fenêtre de jeu ou
perdre le téléphone n'y touchent pas.

Il y en avait une troisième, la case de la fenêtre de mise en route, disparue
avec elle le 2 septembre : voir [D68](#d68---le-premier-lancement-va-droit-aux-appareils).

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

## D63 - L'échelle de qualité allait à l'envers

Le débit vidéo était fixé par palier, la définition et la cadence variaient. La
mesure qui compte est le bit par pixel et par image, parce que c'est ce qu'un
encodeur reçoit vraiment :

| Palier | Définition | Cadence | Débit | bpp |
|:--|:--|--:|--:|--:|
| Basse | 1280x720 | 30 | 2500 kb/s | 0,090 |
| Moyenne | 1920x1080 | 60 | 6000 kb/s | 0,048 |
| Maximale | 3840x2160 | 120 | 16000 kb/s | **0,016** |

Du bas en haut, la définition et la cadence étaient multipliées par quinze, le
débit par six. **Le palier « maximale » recevait cinq fois et demie moins de
bits par pixel que le palier « basse »**, et rendait donc, en mouvement, une
image plus grossière que le palier léger. C'est l'inverse de ce qu'il promet.

La référence pour du H.264 de bonne facture tourne autour de 0,10 bpp à toute
définition : YouTube demande 12 Mb/s en 1080p60, 24 en 1440p60, 53 en 2160p60,
soit 0,096, 0,108 et 0,106.

**Le débit se calcule donc, au lieu d'être fixé** : tant de bits par pixel et
par image, appliqués à la définition et à la cadence réellement retenues. Cela
corrige du même coup une seconde incohérence, à l'intérieur d'un palier : la
définition suit la taille de la fenêtre, si bien qu'un débit fixe servait
grassement une petite fenêtre et affamait une grande.

**Trois mesures ont décidé des nombres**, et aucune n'était devinable :

- **Le jeu rend trente-huit images par seconde** (`dumpsys gfxinfo`, deux
  comptes, deux surfaces). La cadence maximale descend donc de 120 à 60 : les
  cent vingt ne servaient qu'à diviser par deux les bits accordés à chaque image
  qui existe vraiment.
- **Le téléphone a un encodeur matériel H.264 et un H.265**, AV1 en logiciel
  seulement. Le H.265 vaudrait environ 40 % de débit en moins à qualité égale :
  c'est le prochain levier, et la plomberie existe déjà, `ScrcpyOptions.VideoCodec`
  n'attendant qu'un appelant. Il n'est pas activé ici, faute d'avoir mesuré ce
  que coûte son décodage sur un poste modeste, ce qui est précisément la
  clientèle du palier bas.
- **La liaison est le vrai plafond.** Le téléphone est en Wi-Fi 2,4 GHz, norme
  11n, lien annoncé à 144 Mb/s, dont on tire la moitié en pratique. Deux comptes
  ouverts, ce sont deux flux : demander cinquante mégabits par session ne
  donnerait pas une image magnifique mais des pertes et des saccades. D'où un
  plafond par palier, 4, 12 et 25 Mb/s, soit 50 Mb/s au plus pour deux comptes.

**Vérifié sur le téléphone**, à définition et fenêtre identiques :

| | Avant | Après |
|:--|:--|:--|
| Moyenne, 1920x1080 | 6000 kb/s, 0,048 bpp | **11197 kb/s, 0,090 bpp** |
| Maximale, 2880x1620 | 16000 kb/s à 120 ips, 0,029 bpp | **25000 kb/s à 60 ips, 0,089 bpp** |

Trois fois plus de bits par image au palier maximal, et la cadence rendue par le
jeu ne bouge pas : trente-huit à quarante-deux images par seconde avant comme
après.

**Le palier léger allège les pixels, pas leur finesse.** Un quart des pixels et
la moitié de la cadence, mais des bits par pixel du même ordre : brider ceux-là
donnerait du flou sans soulager ni le téléphone ni le poste, dont la charge tient
à la définition et à la cadence. C'est la règle qu'un test énonce désormais.
\n

## D64 - Les pages de succès ne sont pas vides, et l'échec de chargement se dit déjà

Signalé : cliquer un succès depuis les quêtes mènerait à une page vide.

**Ce que c'est.** Sept articles du site, rangés parmi les quêtes, portent un
titre commençant par `[Succès]` : « Des trucs sans intérêt », « Donjons
avancés », « Intérimaire frigostien », et quatre autres. Ce sont de vraies
pages, et ce sont les seules choses nommées « succès » sur lesquelles un clic
navigue quelque part.

**Mesuré sur les sept**, après application du cadrage tel que la fenêtre
l'applique :

| Page | Texte conservé | Images | Paragraphes |
|:--|--:|--:|--:|
| Des trucs sans intérêt | 368 | 18 | 11 |
| Donjons avancés | 2999 | 25 | 22 |
| Donjons trois point cinq | 2079 | 17 | 16 |
| Intérimaire frigostien | 1158 | 0 | 28 |
| Première édition de donjons | 2497 | 22 | 20 |
| La tornade des donjons | 2582 | 22 | 19 |
| Le siège des donjons | 1984 | 18 | 16 |

Aucune n'est vide, et deux ont été ouvertes dans l'application pour le voir. La
plus maigre en texte, « Des trucs sans intérêt », tient sept mille deux cent
trente-cinq pixels de haut : c'est une page d'images.

**Ce qui reste sans effet, en revanche** : la ligne bleue à étoile qui coiffe
les quêtes d'un succès, « Bétapir (1) ». Elle est délibérément inerte, le
conteneur portant `IsEnabled` à faux, donc sans survol, sans curseur de main et
sans sélection. Vérifié au clic réel : rien ne bouge, rien ne se charge.

**Et l'échec de chargement était déjà traité.** On a cru devoir annoncer les
pannes, une fenêtre vide et muette se lisant comme un défaut de l'application.
Éprouvé en mettant au catalogue une adresse injoignable et en l'ouvrant : le
moteur pose sa propre page d'erreur, en français, avec un bouton pour réessayer.
Le message qu'on aurait ajouté n'aurait de toute façon jamais paru, la vue web
étant une fenêtre native qui se dessine au-dessus de tout élément WPF du même
châssis. La règle écrite pour l'occasion a donc été retirée plutôt que laissée
en place sans effet.

**Ce qui est gardé de l'enquête** : le journal disait « succès : false » sans
dire pourquoi. Il dit maintenant l'état rendu par le moteur, `ConnectionAborted`
dans l'essai. Si la page blanche revient, elle sera diagnosticable.

## D65 - Ajouter un compte plutôt qu'expliquer comment cloner

L'application expliquait, marque par marque, où trouver la fonction de clonage
du téléphone. Elle peut le faire elle-même.

> **Corrigé par D70.** La première commande était la mauvaise. Elle créait un
> utilisateur complet, incapable de porter une fenêtre pendant qu'un autre
> compte est ouvert. L'épreuve décrite ici s'était arrêtée à la création du
> profil, sans jamais y ouvrir le jeu.

**Trois commandes suffisent**, et elles ont été éprouvées sur le téléphone avant
d'écrire une ligne :

```
pm create-user <nom>                          -> Success: created user id 10
pm install-existing --user 10 <paquet>        -> Package installed for user: 10
am start-user 10                              -> Success: user started
```

Quinze secondes. Pas de racine, pas de téléchargement, pas de repaquetage :
`install-existing` rend au nouveau profil l'application **déjà présente**,
signée par son éditeur. C'est le mécanisme des comptes multiples d'Android,
celui-là même que l'espace secondaire de la surcouche emploie. Le profil naît en
revanche avec son propre espace de données, vide : le jeu y redemandera ses
ressources et la connexion, ce que la confirmation annonce.

**Ce que le reste de l'application savait déjà faire.** Le profil créé a été
découvert, listé et lancé sans qu'on touche à quoi que ce soit d'autre : toute
la chaîne, de `pm list users` à `am start --user`, ne supposait rien du nombre
de profils. Seule la création manquait.

**Deux défauts trouvés en le construisant, tous deux invisibles au code :**

- **Le registre n'est pas l'état vivant.** Le bouton refusait de travailler en
  annonçant « le téléphone n'est pas connecté » alors que la liste le montrait
  connecté, point vert compris : `IDeviceRegistry` garde le dernier état écrit,
  pas l'état courant. Il lit maintenant la découverte, comme le lancement.
- **ADB recolle les arguments, le téléphone les redécoupe.** `pm create-user
  Compte 3` a créé un profil nommé « Compte ». Les arguments ne sont pas
  transmis un par un : ADB les joint par des espaces et le shell de l'appareil
  les resépare. Le nom est donc cité, et `AndroidShell.Quote` porte la règle,
  apostrophe comprise.

**Limites, mesurées sur le téléphone de référence** : quatre profils en tout,
trois qui tournent à la fois. La place est vérifiée avant de créer, plutôt que
de laisser ADB rendre un refus que personne ne comprend. Et si la surcouche
interdit la création, le message dit où aller à la main : c'est le seul emploi
qui reste aux fiches de marque, dont la fenêtre d'aide est retirée.

**Reste ouvert** : un profil supprimé sur le téléphone garde sa ligne dans la
liste, l'entrée mémorisée survivant à la disparition du profil. Vu pendant les
essais, non corrigé.
\n

## D66 - Oublier un compte dont le profil n'existe plus

Une instance mémorisée survivait à tout, y compris à la suppression de son
profil Android. La liste gardait alors un compte qui n'existe nulle part, et
aucun bouton ne pouvait l'en retirer : seule la rupture d'association, qui
efface tout le téléphone, en venait à bout.

**Deux causes, et la seconde se cachait derrière la première.**

La fusion des instances n'enlève jamais rien, et c'est voulu : un téléphone
débranché doit garder ses lignes. Mais elle ne distinguait pas « ce profil a
disparu » de « on n'a pas pu regarder ».

Une fois cette distinction faite, la ligne restait pourtant. **Le balayage lisait
les profils depuis son cache**, rempli au premier appel et invalidé seulement par
nos propres créations. Un profil supprimé sur le téléphone restait donc connu
indéfiniment. La liste est relue à chaque balayage : la commande est légère au
regard du reste, qui interroge déjà les paquets de chaque profil.

**Ce sur quoi on se fie, et ce qu'on écarte.** Pas l'absence du jeu : les
journaux montrent que `pm list packages` échoue par moments, et une instance
serait oubliée sur un incident passager. La disparition du profil, elle, ne se
constate que sur une liste lue pour de bon, `IsFallback` servant de garde. Un
téléphone qui n'a pas répondu ne figure pas au relevé, et l'on ne conclut donc
rien de son absence.

**Vérifié sur le téléphone** : compte créé par le bouton, profil supprimé depuis
le téléphone, ligne partie au balayage suivant, avec sa trace au journal.
\n

## D67 - Les icônes réelles, la condition de D6 étant remplie

D6 refusait les icônes réelles d'applications et se terminait ainsi :

> À rouvrir si scrcpy publie les icônes, ou si **une extraction ciblée d'entrée
> d'APK devient possible sans transférer le fichier entier**.

C'est le cas, et le motif de D6 était faux pour ce jeu :

| Ce que D6 supposait | Ce que la mesure donne |
|:--|:--|
| Une archive « de plusieurs centaines de mégaoctets » | 14,6 Mo, et elle ne bouge pas |
| Un décodage de `resources.arsc` | Aucun : les icônes sont des PNG en clair, six densités |
| Un transfert du fichier | 51 018 octets, la seule entrée voulue |

Trois commandes, et l'archive reste sur le téléphone : `pm path` dit où elle
est, `unzip -l` dit ce qu'elle contient, `exec-out unzip -p` en tire l'entrée.
441 millisecondes, mesuré, une fois par appareil et par paquet.

**Le choix de l'entrée ne lit aucune ressource.** La convention d'Android nomme
cette image `ic_launcher`, et l'on prend la plus dense des matricielles. Les
morceaux d'une icône adaptative, `_foreground` et `_background`, sont écartés :
montrer l'un seul donnerait une image tronquée. Une application qui ne livre
qu'une icône adaptative en XML ne rend rien, et la liste reste celle d'avant.

**Un piège d'ADB, trouvé en le faisant.** `adb shell` fait passer la commande
par un shell, qui retire les citations. **`adb exec-out` remet les arguments
tels quels**, si bien qu'une apostrophe ajoutée devient une partie du nom de
fichier : citée, l'archive rendait « couldn't open ... : I/O error » ; nue, elle
rend ses cinquante et un kilooctets. La citation est donc appliquée au listage
et retirée de l'extraction, ce qui est aussi plus sûr, rien n'étant réinterprété.

**Ce que la règle du dépôt devient.** `AGENTS.md` interdisait « utiliser une
marque, un logo ou une ressource d'Ankama ». Elle interdit désormais de les
embarquer ou de les redistribuer, ce qui est le fond, et permet d'afficher
l'icône de l'application déjà installée sur l'appareil de l'utilisateur, lue à
l'exécution et gardée dans son cache. Rien n'entre dans le dépôt ni dans
l'exécutable.

**Ce qui reste vrai de D6** : scrcpy ne publie toujours pas les icônes, et aucun
assistant n'est installé sur le téléphone.
\n

## D68 - Le premier lancement va droit aux appareils

Une fenêtre à part accueillait le premier lancement. Elle refaisait ce que le
panneau fait déjà, en moins bien, et la refermer arrêtait l'application.

**Ce qu'elle avait de plus, et ce que ça valait** : une case « ouvrir au
démarrage » par instance, et l'état du téléphone en toutes lettres. La seconde
est reprise dans le panneau, où elle manquait au premier lancement, quand une
pastille de couleur ne dit pas ce qu'elle reproche. La première ne manque pas :
l'ensemble de démarrage est tenu par l'usage, ouvrir une instance l'y met et le
bouton fermer l'en retire, ce que dit [D22](#d22---lensemble-de-démarrage-est-tenu-par-lusage).
Elle en était la troisième écriture, et elle n'existe plus.

**Ce qui la remplace** : le panneau s'ouvre sur l'onglet des appareils, et la
fenêtre d'association vient par-dessus quand aucun téléphone n'est connu. Rien
d'autre n'a de sens à ce moment-là.

L'ouverture de l'association est différée à l'inactivité du répartiteur : lancée
dans la foulée du démarrage, sa boucle modale retiendrait tout ce qui suit, dont
la reprise du suivi de quêtes et la recherche de mise à jour.

**Le premier lancement se reconnaît maintenant à un registre d'appareils vide**,
et non à une marque dans les réglages. C'est le fait qui compte, il se lit déjà,
et le drapeau `SetupCompleted` n'avait plus ni lecteur ni écrivain : il est
retiré. Le schéma ne bouge pas, un champ inconnu d'un ancien fichier étant
ignoré à la lecture.

## D69 - Le chemin de menu se montre, il ne se lit plus

**Premier dessin le 2026-09-02, refait le même jour : voir la fin de l'entrée.**

Les deux aides donnaient le chemin à suivre en une ligne : « Paramètres ›
Applications › Gérer les applications › DOFUS Touch › Économiseur de batterie ».
Juste, mais il faut le lire pour le comprendre, et on le relit à chaque étape.

Il est désormais dessiné : un petit écran par segment, cinq lignes de liste dont
une surlignée, le libellé dessous, un chevron entre deux. On y voit d'un coup
combien d'écrans traverser et où l'on va.

**Rien n'a été rédigé pour cela.** Les fiches de marque écrivent déjà leurs
chemins sous cette forme, et le découpage les lit. Une marque dont le chemin
change de libellé change de dessin sans qu'on y touche, et une marque nouvelle
est illustrée du seul fait d'exister.

**Dessiné dans l'application, et non produit en images.** Les libellés changent
d'une marque à l'autre, « Économiseur de batterie » chez Xiaomi contre
« Batterie » sur Android nu : une image figée ne peut pas les porter, il en
faudrait une par marque et par étape, à refaire à chaque retouche de texte. Le
dessin vectoriel suit le thème, reste net à toute résolution, et rien ne
s'ajoute au dépôt.

**La hauteur de la ligne surlignée est tirée du libellé**, et non au hasard :
l'illustration doit se redessiner à l'identique, sans quoi elle bougerait sous
les yeux de qui rouvre la fenêtre. Deux écrans voisins surlignés à la même
hauteur sont décalés, une suite de lignes alignées ayant l'air d'un dessin figé.
Le dernier écran ne porte pas de chevron : il n'y a rien après, et en tracer un
laisserait croire à une étape manquante.
\n

### Reprise : des écrans qui portent les vrais mots

Le premier dessin montrait des écrans de barres grises, une seule surlignée, et
le libellé **sous** l'écran. On y voyait qu'il y avait des étapes, pas ce qu'il
fallait chercher. Refait le jour même.

**Le découpage change de forme.** Un chemin de N segments donne N-1 écrans, et
non N : on est *dans* « Paramètres » et l'on y touche « Applications ». Le
dernier segment est la ligne du dernier écran, non un écran de plus qu'il
faudrait dessiner vide.

**Chaque écran porte les vrais mots** : une barre de titre avec le chevron de
retour et le nom de l'écran, puis quatre lignes dont celle qu'on touche, avec
son libellé exact sur fond d'accent. Les autres restent muettes : inventer le
reste du menu montrerait ce que la fiche de marque ne dit pas.

**Les libellés reviennent à la ligne au lieu d'être coupés.** Le premier essai
les tronquait, « À propos du téléphone,… », « Options pour les dévelo… », ce qui
ruinait l'objet même du dessin. La hauteur des lignes suit désormais le texte.

## D70 - Un compte doit être rattaché, pas complet

Le bouton « Ajouter un compte » livré en D65 créait un compte qui ne pouvait pas
servir. La commande retenue alors, `pm create-user <nom>`, fait un **utilisateur
Android complet**. Or un utilisateur complet ne peut pas afficher de fenêtre
pendant qu'un autre est au premier plan.

Rien dans D65 ne le disait, parce que la vérification s'était arrêtée à la
création du profil. Elle n'était jamais allée jusqu'à ouvrir une fenêtre dedans.

### Ce qu'Android répond quand on le lui demande

Android publie l'oracle en ligne de commande, et il tranche sans rien lancer :

```
cmd user is-visible-background-users-supported   -> false
cmd user is-user-visible --display 717 14        -> false   (utilisateur complet)
cmd user is-user-visible --display 718 15        -> true    (profil rattaché)
```

Mesuré sur un Xiaomi 23078PND5G sous Android 16, afficheur virtuel créé par
scrcpy.

### Pourquoi le code de sortie ne pouvait pas le voir

C'est le piège, et il vaut d'être écrit noir sur blanc :

| Profil | `am start -W` | `LaunchState` | Attente | Fenêtre |
|:--|:--|:--|--:|:--|
| Utilisateur complet 14 | `Status: ok` | `UNKNOWN (0)` | 69 917 ms | aucune |
| Profil rattaché 15 | `Status: ok` | `COLD` | 3 929 ms | le jeu |

**`am start` annonce un succès là où rien ne s'affichera jamais.** Il pend
jusqu'à ce que l'afficheur disparaisse, puis rend `ok`. Se fier à lui revient à
promettre une fenêtre qui ne viendra pas ; c'est exactement ce que faisait
l'application.

### Ce qui est retenu

`pm create-user --profileOf <principal> --managed <nom>`. Le profil est
**rattaché** au compte principal, donc visible dès que celui-ci l'est, sur
n'importe quel afficheur. Vérifié jusqu'à l'écran de connexion du jeu, capture à
l'appui.

L'identifiant du parent n'est pas câblé : il est pris sur le profil que le
téléphone déclare principal.

### Les plafonds, qui n'étaient pas ceux que l'application lisait

```
pm get-max-users                  -> Maximum supported users: 4
pm get-max-running-users          -> Maximum supported running users: 3
pm create-user --profileOf 0 --managed  (le second)
   -> Cannot add more profiles of type android.os.usertype.profile.MANAGED
      for user 0 (code 6)
```

Un profil géré par compte principal, un clone par compte principal. Soit **trois
fenêtres au maximum** sur ce téléphone : le principal, un clone, un profil géré.
L'application lisait `get-max-users` et croyait à quatre places. Elle annonce
désormais le vrai refus avant de le provoquer.

### Ce que devient Second Space

Il ne convient pas, et l'affirmation contraire d'AGENTS.md est corrigée. Second
Space est un utilisateur complet : il remplace l'écran au lieu de s'ouvrir à
côté. Les voies qui marchent sont les profils rattachés, ce que produisent les
applications dupliquées, le profil professionnel, Shelter et Island.

### Le mode pause, jamais lu jusqu'ici

`FLAG_QUIET_MODE` (0x80) n'apparaissait nulle part dans le dépôt. C'est
pourtant l'interrupteur du profil professionnel et **la fonction principale** de
Shelter et d'Island. Un profil en pause se liste comme les autres et ne lance
rien : le lancement échouait sans que rien n'explique pourquoi.

Le drapeau est lu, le profil écarté avant le lancement, et le message dit de le
rallumer sur le téléphone. L'application ne propose pas de le faire à sa place :
`cmd user set-quiet-mode` n'existe pas, vérifié.

### Ce qui reste déduit plutôt que mesuré

Le Dossier sécurisé de Samsung est un profil Knox, donc un profil rattaché : il
devrait relever de la voie qui marche. Faute d'appareil Samsung, ce n'est pas
vérifié, et un refus de permission y reste possible. C'est pourquoi ce refus est
désormais nommé pour ce qu'il est plutôt que traduit en « application absente ».

## D71 - Les réglages fins se replient, et le débit se juge

L'utilisateur voulait descendre plus bas que les trois paliers, et trois choses
absentes du panneau : le son du téléphone sur le PC, l'extinction de son écran,
la coupure de ses animations. Consigne : simple et complet.

### Un quatrième palier qui n'est pas un barreau

`Personnalisé` s'ajoute aux trois paliers mais ne prolonge pas leur échelle :
c'est une sortie de route. La règle des trois, qui existe parce que deux voisins
indiscernables ne font qu'hésiter, reste vraie pour le chemin ordinaire. Le test
qui la gardait a été récrit pour compter les paliers automatiques, non les
valeurs de l'énumération.

Le panneau reste replié tant qu'on ne choisit pas ce palier. C'est ce qui tient
la promesse de simplicité : quatre lignes de plus, visibles en permanence,
auraient chargé le panneau de ce que la plupart des gens n'ont pas à savoir.

**Le DPI n'y figure pas.** Il se règle déjà, plus bas, sous le nom de « distance
dans le jeu », qui dit ce qu'il fait au joueur plutôt que ce qu'il est. La
capture de référence l'appelait « DPI 320 (fin) » ; la formule actuelle est
meilleure, et l'afficher deux fois aurait fait deux réglages pour une chose.

**L'intervalle d'image-clé est écarté**, seul des cinq de la référence dont
l'effet ne se voit pas en usage ordinaire. Le codec, lui, s'ajoute : il fait
bien davantage pour l'image et s'explique en trois mots.

### La couture, qui évitait de toucher au lanceur

`GameLauncher` ne tient pas le palier mais un `QualityProfile`, et tout passe par
cet objet : le débit y est recalculé à chaque taille de fenêtre retenue. Il a
donc suffi de faire porter au profil les valeurs choisies. Une seule ligne du
lanceur change.

### Le débit ne se règle pas en mégabits, et c'est le point

Le premier jet copiait l'application de référence : une ligne « Débit » en
mégabits, rendue telle quelle par un `FixedKbps` qui court-circuitait le calcul.
C'était défaire une leçon que ce code avait déjà apprise, et qui est écrite deux
lignes au-dessus de l'endroit où le raccord a été fait :

> Le débit s'en déduit aussi, et pour la même raison : un débit fixe servait
> grassement une petite fenêtre et affamait une grande.

La définition de l'afficheur suit ici la taille de la fenêtre. Un débit absolu
n'y a pas de sens. La ligne est donc devenue **Finesse d'image**, en bits par
pixel, qui est déjà l'unité des trois paliers et garde son sens à toute taille.

Le plafond reste, et c'est celui du palier le plus haut. Il ne protège pas de
l'utilisateur mais de la liaison.

### Ce que la liaison encaisse, et qui n'existe pas chez un miroir simple

L'application de référence pilote un seul miroir. DT Hub ouvre plusieurs
fenêtres sur **un seul téléphone et une seule liaison**. Juger un flux isolé
dirait « confortable » pendant que le téléphone s'étrangle : trois comptes à
vingt-cinq mégabits en demandent soixante-quinze, là où l'appareil de référence
en rend une soixantaine en Wi-Fi 4 sur 2,4 GHz.

D'où la seconde ligne, sous le verdict :

> au plus 11,2 Mb/s par fenêtre, 22,4 Mb/s à 2 comptes

Le compte est celui du téléphone le plus chargé, non le total tous appareils
confondus : c'est sa liaison qui cède la première. Il se rafraîchit à chaque
ouverture et fermeture de fenêtre, faute de quoi la ligne annonçait le coût
d'une seule alors que deux tournaient, et justement au moment où elle sert.

### « Définition » devient « Définition max. »

Le libellé mentait. La définition retenue suit la taille de la fenêtre ; la
liste ne fixe qu'un plafond. Mesuré : plafond à 2160, fenêtre à 80 % d'un écran
4K, afficheur réellement créé en **2880 x 1620**. Chez le miroir de référence la
résolution est la résolution ; ici non, et le mot doit le dire.

### Le verdict en bits par pixel

Quatre nombres nus ne se jugent pas. Seize mégabits sont généreux en 720p et
misérables en 2160p, et c'est exactement l'erreur dans laquelle ce projet est
tombé : les paliers avaient le débit à l'envers, « maximale » recevant cinq fois
et demie moins de bits par pixel que « basse ». Chaque nombre pris isolément
semblait pourtant raisonnable.

D'où cette ligne, sous les quatre réglages :

> 0,096 bit par pixel et par image, confortable

Les seuils viennent des références publiées par YouTube pour du H.264 de bonne
facture, toutes autour de 0,10. Le codec entre dans le calcul : H.265 demande
environ un tiers de bits en moins à qualité égale, et sans en tenir compte le
verdict aurait puni celui qui vient d'améliorer son réglage.

### Deux codecs, parce que le téléphone n'en encode que deux

```
scrcpy --list-encoders
--video-codec=h264  c2.mtk.avc.encoder      (hw) [vendor]
--video-codec=h265  c2.mtk.hevc.encoder     (hw) [vendor]
--video-codec=av1   c2.android.av1.encoder  (sw)
--video-codec=vp8   c2.android.vp8.encoder  (sw)
```

AV1 n'a qu'un encodeur logiciel. Le proposer serait un piège : l'encoder ainsi à
soixante images par seconde coûte bien plus qu'il ne rend. Beaucoup d'interfaces
scrcpy l'offrent quand même. Vérifié après coup, `--video-codec=h265` prend bien
`c2.mtk.hevc.encoder`.

### Le son est celui du téléphone, pas du compte

Android ne sait pas isoler le son d'une application. Avec trois fenêtres sur un
téléphone, l'activer partout donnerait trois fois le même flux, c'est-à-dire un
écho. Une seule session par appareil le porte donc, la première ouverte, et la
règle se déduit de l'état déjà connu : une session est-elle déjà ouverte sur ce
téléphone ?

Relevé sur les lignes de commande, deux comptes ouverts :

```
session 1 : --turn-screen-off --video-codec=h264
session 2 : --no-audio --turn-screen-off --video-codec=h264
```

### Les animations, seul réglage qui laisse une trace

Ce ne sont pas des options de session mais trois valeurs globales d'Android,
écrites par ADB, qui survivent à la fermeture de DT Hub. D'où la règle : **on
relit avant d'écrire**, on garde ce qu'on a trouvé, et on le rend. Supposer que
tout valait 1 remettrait à 1 le téléphone de qui les avait réglées autrement.

La restauration est dans le `finally` de la fermeture et sans jeton
d'annulation : rendre au téléphone ce qu'on lui a pris ne doit dépendre ni de la
réussite de la fermeture, ni de la patience de qui a demandé l'arrêt.

Aucune bascule ADB n'existe pour le mode pause d'un profil, mais l'écriture des
échelles d'animation, elle, passe : vérifié, et la relecture rend bien 0.0 puis
1.0 après fermeture.

**Ce qui n'est pas garanti est dit.** Une fin brutale de l'application laisse les
animations coupées. L'infobulle l'annonce, et dit où les rétablir, plutôt que de
promettre ce qui ne peut pas l'être. Le gain est par ailleurs modeste sur un
afficheur qui ne porte que le jeu : ces animations sont celles du système.

### Un travers d'interface, corrigé

Les listes déroulantes affichaient `IntChoice { Label = 1920 x 1080, ... }` :
`DisplayMemberPath` ne prend pas avec le gabarit de sélection du thème. Un
`ItemTemplate` explicite règle l'affichage de la liste et celui de la valeur
choisie.

### Reprise : les réglages fins sortent de la carte

Dépliés sous les paliers, les quatre réglages faisaient gagner deux cents pixels
à la carte et repoussaient tout ce qui suit. Choisir un palier changeait la
géométrie du panneau, ce qui est exactement ce qu'un choix de palier ne doit pas
faire.

Ils vivent maintenant dans une bulle, ouverte par un rouage qui ne paraît qu'au
palier personnalisé. Une bulle ne prend aucune place dans la mise en page :
mesuré, l'intertitre « Distance dans le jeu » tombe au même pixel que le palier
choisi soit « Moyenne » ou « Personnalisé ».

Le rouage est dessiné, non emprunté : sa géométrie est calculée, huit dents,
rayon extérieur 8,4 et intérieur 6, trou de 2,9, dans une boîte de vingt. La
fiche du projet interdit d'embarquer une ressource tierce, et une icône de jeu
d'icônes en serait une.

**« Maximale » devient « Haute ».** Le nom avait été retiré en version 8 quand ce
palier-là avait disparu ; il désigne maintenant le plus haut des trois, qui reste
le même. L'énumération, elle, garde `Maximum` : renommer une valeur enregistrée
n'apporterait rien et casserait les fichiers existants.

**Les libellés du téléphone perdent leur répétition.** « Son du téléphone sur le
PC », « Éteindre l'écran du téléphone », « Couper les animations du téléphone »
disaient trois fois ce que l'intertitre disait déjà. Ils deviennent « Son renvoyé
sur le PC », « Écran éteint », « Animations coupées ».

**Un garde-fou manquant.** `OnUpdatesAutomaticChanged` était le seul gestionnaire
de réglage sans le test de chargement : lire les préférences réécrivait aussitôt
le fichier avec ce qu'on venait d'y trouver. Sans conséquence visible, mais c'est
une écriture pour rien à chaque ouverture du panneau.

### Le nom du jeu, là où il lève une ambiguïté

L'application est faite pour un jeu et ne le disait qu'à son titre de fenêtre.
Elle le dit maintenant à trois endroits : sur la ligne du produit dans l'en-tête,
où il ne coûte aucune hauteur et se voit sur les trois onglets ; sur les fenêtres
« de jeu », qui se distinguent ainsi du panneau lui-même ; et au-dessus de la
liste des comptes, qui sont bien des installations du jeu.

Nommer n'est pas embarquer : la fiche interdit d'inclure une marque, un logo ou
une ressource d'Ankama, ce qui vise les fichiers, non le mot. Le titre de
l'assemblage le nomme déjà, et la mention « projet indépendant, sans lien avec
Ankama » reste sous les yeux.

**Il n'est pas mis dans la section « Sur le téléphone », et c'est délibéré.**
Les trois interrupteurs y auraient été faux :

| Interrupteur | Ce qu'il touche vraiment |
|:--|:--|
| Son renvoyé sur le PC | le son de l'appareil entier, Android ne sachant pas l'isoler |
| Écran éteint | la dalle du téléphone |
| Animations coupées | les trois échelles globales d'Android |

Écrire « son de DOFUS Touch » aurait promis une isolation qui n'existe pas, et
masqué justement ce que l'infobulle explique : toutes les applications du
téléphone s'entendent ensemble.

### Le rouage remonte sur la ligne du titre

Placé après les paliers, il retombait seul à la ligne suivante et faisait gagner
une rangée à la carte. Sur la ligne de l'intertitre, aligné à droite, il tient
dans la hauteur que celui-ci occupe déjà : mesuré, l'intertitre « Distance dans
le jeu » ne bouge pas d'un pixel selon le palier coché.

### « Écran éteint » disait trop peu

Raccourci de « Éteindre l'écran du téléphone » à « Écran éteint » pour ôter une
répétition, le libellé a été compris comme parlant des fenêtres de jeu. Il
redevient « Écran du téléphone éteint » : la répétition coûtait moins cher que
l'ambiguïté.

Deux vérifications que la question a provoquées, et qui manquaient :

**L'image survit.** Ce qui était écrit dans l'infobulle sans avoir été prouvé
l'est maintenant : afficheur virtuel, `--turn-screen-off`, jeu lancé, la fenêtre
montre l'écran de connexion complet pendant que `mWakefulness` vaut `Dozing`.

**Le gain est modeste, et l'infobulle le dit.** Sans l'option, la dalle passe
quand même en veille pendant la session : `--keep-active` ne la retient pas.
L'aide de scrcpy le décrit comme « garder l'écran allumé en simulant une
activité », et avec un afficheur virtuel cette activité porte sur cet afficheur,
non sur la dalle. L'option ne fait donc qu'éteindre **tout de suite** au lieu
d'attendre le délai de veille, ce qui compte sur un téléphone réglé pour rester
allumé longtemps, ou branché.

Le commentaire du constructeur d'arguments affirmait que les deux options ne se
contredisaient pas parce que « l'un empêche l'appareil de se mettre en veille,
l'autre éteint sa dalle ». C'était faux sur le premier point, et corrigé.

### Deux options retirées faute de rendre quelque chose

**Éteindre l'écran du téléphone.** Mesuré : sans l'option, la dalle passe quand
même en veille pendant la session. `--keep-active` ne la retient pas, son
activité simulée portant sur l'afficheur virtuel. L'option n'avançait donc que
l'extinction de quelques minutes.

**Couper les animations.** Quatre lancements alternés, temps rendu par
`am start -W` sur afficheur virtuel :

| Échelles | Temps de lancement |
|:--|--:|
| 1,0 | 690 ms, 588 ms |
| 0,0 | 633 ms, 604 ms |

L'écart entre deux essais au même réglage dépasse l'écart entre les deux
réglages : la coupure ne rend rien de mesurable. Elle coûtait en revanche une
mutation globale du téléphone, qui survit à un plantage de l'application, plus un
service de cent soixante lignes et ses tests.

La section « Sur le téléphone » n'ayant plus qu'une case, elle disparaît aussi :
la case rejoint les lignes isolées du bas. Le panneau y gagne une carte entière
de hauteur.

### Le son ne peut pas s'appeler « son du jeu »

La demande était « son du jeu renvoyé sur le PC ». Ce serait faux, et pas d'un
cheveu : Android ne sait pas isoler le son d'une application, et scrcpy capte la
sortie de l'appareil entier. Avec trois comptes ouverts, on entend les trois
mélangés, plus les notifications.

Le libellé promettrait donc une singularité qui n'existe pas, et masquerait ce
que l'infobulle explique. Il devient « Son du téléphone renvoyé sur le PC », qui
nomme la seule chose vraie.

### La définition, ce qu'elle rend et ce qu'elle ne rend pas

Signalé depuis l'usage : définition poussée au maximum, ça rame, et rien ne
change à l'écran. Deux mesures, même scène, débit calculé comme l'application le
ferait.

**Dans une fenêtre de 1428 de haut :**

| | 1920 × 1080 | 2560 × 1440 |
|:--|--:|--:|
| Débit demandé | 11 197 kb/s | 19 907 kb/s |
| Images par seconde | 19 à 28 | 13 à 33 |
| Écart moyen entre les deux images | | **0,63 sur 255** |
| Énergie de contours | 1,37 | 1,42 |

Soixante-dix-huit pour cent de débit pour un quart de pour cent d'image.

**Dans une fenêtre de 1800 de haut**, le même essai donne un écart de 3,14 sur
255 et une énergie de contours de 4,43 contre 5,03, soit treize pour cent de
plus. Le texte y est visiblement plus net.

**La première conclusion était donc trop générale**, et brider le palier « Haute »
à 1080, ce qui avait été envisagé, aurait ramolli les grandes fenêtres. La règle
juste est que la définition doit suivre la taille des fenêtres, ce que
`DisplayLadder` fait déjà.

### Le piège était ailleurs, et il est invisible

`DisplayLadder.Choose` retient **le premier palier au-dessus de la fenêtre**,
puis le plafond de qualité ne fait que le rabaisser. Un plafond au-delà de la
taille des fenêtres ne demande donc rien de plus : 2160 et 1440 donnent le même
afficheur dans une fenêtre de 1428.

Rien ne le disait. On croyait monter en finesse, on ne montait rien, et l'on
pouvait payer le double de débit en s'arrêtant à un palier intermédiaire. D'où la
troisième ligne du panneau, qui lit la définition sur la ligne de commande de la
session en cours, seul témoin qui ne puisse pas mentir, et annonce quand le
plafond est sans effet.

### Ce que valent les quatre réglages fins

| Réglage | Verdict |
|:--|:--|
| Finesse d'image | agit directement sur l'image et le débit |
| Codec vidéo | H.265 rend mieux à débit égal, encodeur matériel |
| Définition max. | agit, mais seulement jusqu'à la taille des fenêtres |
| Cadence | le jeu rend 19 à 33 images ici : 45 et 60 se valent |

## D72 - Des sessions nommées, posées au-dessus de l'ensemble de démarrage

Ouvrir tantôt deux comptes, tantôt un seul, demandait de refaire le geste à
chaque fois. Il fallait pouvoir retenir des ensembles nommés, et en désigner un
pour le démarrage.

### Ce qui a décidé de la forme

Le lancement ne filtre que sur un drapeau, `LaunchEnabledAsync` faisant
`instances.Where(i => i.IsEnabled)`. Une session peut donc se poser **au-dessus**
sans toucher au lancement : l'appliquer, c'est écrire `IsEnabled`. Rien du chemin
d'ouverture n'a changé.

**Il n'y a plus de cases à cocher**, et c'est une décision antérieure du projet,
D-e96e931 : « les cases à cocher quittent le configurateur ». Une session ne peut
donc pas être « les comptes cochés ». C'est ce qui lui donne sa forme : elle
retient **les comptes ouverts**, ce que le mot session dit déjà.

Et comme l'ensemble de démarrage dérive tout seul, un lancement réussi y ajoutant
les comptes ouverts et le bouton « fermer » les en retirant, une session doit
être une liste à part. Déduite à la volée, elle se serait réécrite d'elle-même et
n'aurait rien retenu.

### Choisir ouvre pour de bon

Faute de cases, écrire seulement l'ensemble de démarrage ne montrerait rien à
l'écran : on cliquerait sans savoir s'il s'est passé quelque chose. Choisir une
session ferme donc ce qui n'en fait pas partie et ouvre ce qui manque, après une
confirmation quand des fenêtres sont ouvertes. Un clic dans une liste n'est pas
un consentement à fermer une partie en cours.

### Éprouvé sur le matériel

Les deux comptes ouverts et cochés, session « Solo XSpace » enregistrée puis
désignée pour le démarrage. Au redémarrage, le journal dit :

```
Session « Solo XSpace » retenue : 1 compte(s).
Lancement terminé : 1 fenêtre(s) ouverte(s), 0 problème(s).
```

Une seule fenêtre là où deux rouvraient : la session restreint bien le démarrage.

### Un effet de bord ramassé

La fenêtre de premier lancement supprimée en D68, `ShowSelection` n'avait plus
aucun consommateur : la propriété, sa colonne et sa case étaient du code mort que
personne n'avait vu partir. Retiré.

### Une saisie de texte, qui manquait

`IDialogService` ne savait qu'informer, avertir et faire confirmer. Nommer une
session demande une ligne de texte, d'où `PromptText` et une petite fenêtre
modale, sur le modèle de celle de l'appairage.

### Reprise : la ligne des sessions tient sur une seule

L'intertitre au-dessus et les commandes en dessous faisaient quatre-vingts
pixels de haut, pris sur la liste des comptes qui, elle, en a besoin. Tout
revient sur une ligne, et le bloc tombe à quarante-sept.

**L'intertitre disparaît, remplacé par une invite dans le champ vide.** Elle dit
ce que l'intertitre disait, et davantage : « Choisir une session » quand il y en
a, « Aucune session enregistrée » sinon. Un champ vide ne distinguait pas les
deux, et c'est justement la question qu'on se pose en le voyant. Elle se pose
par-dessus la liste et ne prend aucune place à elle.

Deux pièges rencontrés en chemin, tous deux visibles seulement à l'écran : le
style « Muted » autorise le retour à la ligne, si bien que l'invite gonflait la
liste d'une ligne entière ; et l'intertitre gardé en ligne volait à la liste la
largeur qui lui manquait ensuite pour afficher un nom de session.


### Reprise : un profil emporte l'état, pas seulement les comptes

La première forme ne retenait qu'une liste de comptes. Ce n'était pas la demande :
ouvrir un profil doit lancer certains comptes **à certaines positions et avec
certains réglages**. Le profil porte donc la géométrie de chaque fenêtre, la
qualité et sa personnalisation, la distance dans le jeu, l'ancrage et la taille.

**L'instantané se prend dans le document**, non en paramètres : la géométrie y
est déjà, relevée juste avant par `CaptureGeometriesAsync`, et les réglages y
vivent en permanence. Les passer de l'extérieur aurait ouvert la porte à un
profil qui retient autre chose que ce que l'écran montre.

**Un ordre qu'il ne faut pas inverser.** `CloseAllAsync` commence par relever la
géométrie des fenêtres ouvertes. Appliquer le profil avant de fermer aurait donc
fait écraser ses positions par celles qu'on ferme. L'ordre est : fermer,
appliquer, lancer. La première version faisait l'inverse.

**Un profil ne se met à jour que sur commande.** Déplacer une fenêtre ne le
modifie pas : il faut réenregistrer. C'est ce qui le distingue de l'ensemble de
démarrage, qui lui suit les gestes.

**La ligne quitte le flux.** Dépliée dans la page, elle prenait quarante-sept
pixels à la liste des comptes ; dans une fenêtre de 580 de haut, le second compte
s'en trouvait coupé. Elle passe derrière un bouton « Profils », sur la ligne du
bouton d'association qui avait de la place à droite. Vérifié à la capture : les
deux comptes tiennent désormais à l'écran.

Deux défauts trouvés en éprouvant, tous deux invisibles à la compilation :

- `QuietButton` cible `Button`. L'appliquer à un `ToggleButton` lève au
  chargement de la fenêtre, et l'application ne démarrait plus. Le bouton a
  maintenant son gabarit.
- Une liste déroulante nomme ses entrées d'après le type de l'objet : relevé à
  l'automatisation, elles s'appelaient toutes
  « DtHub.App.ViewModels.LaunchProfileRowViewModel ». Le gabarit d'affichage ne
  corrige pas ce nom-là, et c'est celui qu'un lecteur d'écran prononce. Corrigé
  par un `ToString`.

### Reprise : la liste est montrée, et choisir n'ouvre plus

Le champ replié cachait ce qu'on avait, et surtout **le choisir ouvrait le
profil pour de bon** : `OnSelectedProfileChanged` appelait l'ouverture, qui
ferme les fenêtres de jeu. Un clic d'exploration coûtait donc une partie en
cours, et la confirmation ne rattrapait qu'à moitié, arrivant après le geste.

La bulle montre désormais une ligne par profil, avec à droite de quoi l'ouvrir,
le désigner pour le démarrage et le supprimer. **Ouvrir est un geste à part**,
et la sélection n'existe plus : la liste peut se reconstruire à chaque balayage
sans rien déclencher, ce qui supprime du même coup le garde-fou qu'il fallait
tenir pour l'en empêcher. Le `ToString` du paragraphe précédent perd sa raison
d'être avec la liste déroulante ; il reste, sans coût, pour l'automatisation.

**« Créer » dit ce qu'il retient.** « Enregistrer » ne laissait rien deviner :
on croyait ne retenir que des comptes, et l'ouverture replaçait les fenêtres et
changeait la qualité. La fenêtre de saisie porte donc une phrase construite sur
l'état du moment, non une liste figée, pour qu'on y reconnaisse son propre
réglage. `PromptText` a gagné un texte explicatif et un libellé de bouton.

**Une mesure de largeur.** La bulle à 380 unités débordait de vingt-cinq pixels
sur la gauche de la fenêtre : à cent cinquante pour cent, le panneau ne fait que
360 unités de large. Ramenée à 340, elle s'aligne sur son bord droit.

## D73 - Un mode onglets, et ce que la sonde a permis d'affirmer

Loger plusieurs comptes dans un seul cadre, comme un navigateur, supposait de
ré-parenter des fenêtres SDL. Rien de tel n'avait jamais été fait ici, et la
faisabilité ne se lisait pas dans le code.

### La sonde d'abord, l'interface ensuite

Relevé sur une vraie session, avant d'écrire une ligne d'interface :

```
avant  : style 0x16CF0000  parent 0
apres  : parent 20252514  attendu 20252514  arrime: True
pointeur en 560,460 -> fenetre 9766672  cible: True
rendue : parent 0  style 0x16CF0000
```

La fenêtre devient fille, continue de rendre l'image, reçoit le pointeur selon
Windows lui-même, et ressort intacte. C'est ce qui a autorisé la suite.

La mesure du focus clavier a dû être abandonnée : `AttachThreadInput` et
`GetFocus`, joints à `SetParent`, forment la signature d'une injection de
frappes, et l'antivirus a bloqué le script entier. Ni exclusion ni désactivation.

### L'état d'origine est retenu, jamais recalculé

`Dock` mémorise style, parent et géométrie ; `Undock` les rend. Une fenêtre
rendue avec un style deviné ne se comporterait plus comme les autres.

### Trois pièges, tous invisibles à la compilation

**Le fil d'interface.** Créer le cadre depuis la chaîne asynchrone du lanceur
lève « le thread appelant doit être en mode STA ». Tout geste d'interface passe
désormais par le répartiteur.

**La surveillance périodique.** `Watch` appliquait `EnforceAspect` à toutes les
sessions, y compris logées : toutes les demi-secondes, elle défaisait la pose du
cadre et le jeu revenait se coller de travers. Deux implémentations de la pose
ont été essayées avant de comprendre que ce n'était pas la pose qui échouait,
mais quelqu'un qui l'annulait derrière.

**La densité.** Convertir soi-même les unités de WPF en pixels donnait faux sur
un écran à cent cinquante pour cent. La zone client est demandée à Windows.

### Ce que le cadre ne peut pas faire

Rien ne se dessine par-dessus le jeu : une fenêtre native se peint au-dessus de
tout élément WPF du même châssis, comme D33 le notait déjà. La barre d'onglets
est donc au-dessus de la zone de jeu, jamais dessus.

### Reprise : le cadre prend la forme du jeu, il ne la subit pas

scrcpy verrouille le rapport de ce qu'il rend. Une zone d'accueil d'une autre
forme lui laisse donc forcément une bande noire, et la première pose ne faisait
que la **répartir** de part et d'autre au lieu de la supprimer.

Le cadre adopte maintenant le rapport de l'afficheur à chaque
redimensionnement : sa hauteur vaut la largeur de la zone divisée par ce
rapport, plus l'encombrement du châssis. C'est le calcul que
`WindowManagerService.EnforceAspect` fait déjà pour les fenêtres libres, à la
barre d'onglets près. S'il n'y a plus de place en hauteur sur l'écran, c'est la
largeur qui cède : rogner encore la hauteur donnerait un cadre écrasé, et il
avait déjà été trouvé trop court une fois.

**Recréer l'afficheur à la taille exacte de la zone est écarté.** Ce serait le
seul moyen d'avoir des pixels justes plutôt qu'une image mise à l'échelle, mais
`--new-display` crée l'afficheur *et* y lance l'application : chaque
redimensionnement du cadre redémarrerait le jeu.

La conversion de coordonnées passe désormais par `PointToScreen` sur la zone
d'accueil et sur la fenêtre, dont la différence donne l'origine en pixels dans
la zone client. Le calcul précédent additionnait une hauteur d'onglets en
unités de WPF et une marge déjà mise à l'échelle ; il tombait juste par
coïncidence à cent cinquante pour cent.

### Reprise : un onglet ne survit pas à sa session

Rien ne retirait l'onglet quand la session fermait. Le cadre croyait donc le
compte encore logé, `Attach` refusait de le reloger, et rouvrir un profil
faisait reparaître la fenêtre **libre, par-dessus le cadre**, barre de titre
comprise. Le défaut ne se voyait qu'au deuxième lancement.

Les fermetures voulues détachent maintenant l'onglet elles-mêmes, sans attendre
l'entretien périodique : l'ouverture d'un profil enchaîne fermeture et
relancement dans la même chaîne, et un rendez-vous toutes les demi-secondes
arriverait trop tard. `Watch` garde un filet, `KeepOnly`, pour les sessions qui
meurent sans passer par nous : fenêtre fermée à la main, téléphone débranché.

Le cadre se referme quand son dernier onglet le quitte. Un cadre vide n'a rien
à montrer et ne dit pas ce qu'il attend ; sa position n'étant pas retenue, le
rouvrir ne coûte rien.

### Le glissement des onglets, éprouvé

Le glissement était écrit mais jamais éprouvé. Il l'a été, et il marche du
premier coup : glisser « Principal » sur la moitié droite de « XSpace » range
les onglets dans l'ordre XSpace, Principal, et la liste des comptes suit, le
champ `order` du document passant à 0 et 1.

Le premier essai avait pourtant conclu à une panne. La sonde était en cause :
son `Add-Type` avait échoué sur un fichier temporaire refusé, le type appelé
ensuite n'était pas celui qu'elle croyait, et aucun mouvement de souris
n'atteignait la fenêtre. **Un outil de mesure muet vaut un faux négatif** : la
sonde dit maintenant ce qu'elle fait, et l'appelant relance quand elle échoue.

### Reprise : le geste se voit, et le cadre s'attrape par tous les bords

Le glissement marchait mais ne montrait rien. On lâchait à l'aveugle : rien ne
disait de quel côté le dépôt tomberait, et viser à côté d'un onglet ne faisait
rien sans dire pourquoi. Trois corrections, toutes visibles à l'écran :

- **Un trait d'accent** paraît à gauche ou à droite de l'onglet survolé. Les
  deux gouttières qui le portent sont là en permanence, même vides : les faire
  apparaître au survol décalait toute la barre de trois pixels au moment précis
  où l'on vise.
- **La barre entière reçoit le dépôt**, et pas seulement les onglets : lâcher
  après le dernier range en fin de liste.
- **L'onglet part à l'instant où on lâche.** Il attendait le retour de
  l'enregistrement, et le geste paraissait n'avoir rien fait pendant ce trajet.
- Le seuil de glissement ne regarde plus que l'écart horizontal : la barre est
  horizontale, et le tremblement vertical d'un simple clic partait en glissement.

**Le redimensionnement passe par WM_SIZING.** Le cadre gardait sa forme en se
corrigeant après coup, ce qui rendait le bord du bas inerte : la hauteur était
aussitôt recalculée depuis la largeur, et la fenêtre paraissait résister à la
souris. Windows demande sa taille à la fenêtre pendant l'étirement, bord par
bord ; y répondre laisse attraper le cadre par n'importe quel bord, comme une
fenêtre de jeu libre. Tirer un côté commande la hauteur, tirer le haut ou le bas
commande la largeur, et le bord opposé à celui qu'on tire ne bouge pas, sans quoi
la fenêtre glisserait sous la souris au lieu de s'étirer.

La règle est sortie de la fenêtre, dans `AspectSizing` : c'est un calcul, et un
calcul se vérifie sans ouvrir d'interface. Sept épreuves le tiennent, dont une
qui balaie les huit bords et vérifie que la zone de jeu garde son rapport.

### Reprise : ce qu'un profil doit retenir de plus

Trois manques, cherchés en relisant le document de réglages ligne à ligne :

- **Le son du jeu renvoyé sur le PC** et **le presse-papiers partagé**. On ne
  joue pas de la même façon avec et sans le son.
- **La place du cadre à onglets.** Les positions retenues pour chaque compte ne
  disent rien du cadre : une fenêtre logée n'a plus de place à elle. Un profil en
  onglets rouvrait donc son cadre là où Windows voulait bien le mettre. Elle
  n'est retenue que si le profil loge quelque chose, faute de quoi ouvrir un
  profil sans onglets déplacerait le cadre d'un autre.

Le reste du document a été écarté à dessein : les paliers de taille, le paquet du
jeu et les raccourcis sont des préférences générales, pas l'état d'une séance ;
la définition et la densité de l'afficheur virtuel se déduisent à l'ouverture de
la taille de la fenêtre et de la distance.

**Un défaut trouvé au passage.** Le relevé des géométries prenait aussi les
fenêtres logées, dont le rectangle est celui qu'elles occupent dans le cadre. Les
sortir des onglets les faisait reparaître au milieu de l'écran. Elles en sont
maintenant écartées.

### La souris de l'utilisateur n'est pas la nôtre

Le glissement n'a pas pu être remesuré tout de suite : l'utilisateur était revenu
à sa machine, Firefox au premier plan, et les clics injectés se perdaient. Le
diagnostic a coûté plusieurs essais avant que la fenêtre de premier plan ne le
dise. **Vérifier qui tient la souris avant de conclure à une panne**, et ne pas
la lui disputer.

## D74 - Signaler une erreur au site, sans jamais parler à sa place

Les guides viennent de papycha.fr, et leurs pages portent en pied un formulaire
« Remonter une erreur ». On le lisait dans notre fenêtre sans pouvoir s'en
servir : le cadrage masque `footer.papycha-article-footer`, et le formulaire est
dedans.

### Ce que l'application remplit, et ce qu'elle ne remplit pas

**La zone et la quête, et rien d'autre.** Le formulaire demande où se trouve
l'erreur ; l'application y porte « Astrub › La découverte d'un destin », dans les
termes du site et dans la forme du fil d'Ariane de la fenêtre.

Elle y écrivait d'abord le rang de l'étape et le début du paragraphe :
« Étape 3 / 12 : « … » ». **Ce rang est une numérotation qui n'existe que chez
nous** : le site ne numérote pas ses paragraphes, et le repère ne désignait donc
rien pour qui reçoit le signalement.

**La description reste vide.** C'est ce que le lecteur a vu, et l'écrire pour lui
reviendrait à signaler quelque chose qu'il n'a pas dit. **Rien n'est envoyé** :
la fenêtre montre le formulaire du site, c'est le lecteur qui appuie. Le champ
anti-robot du site n'est pas touché, et le repère n'écrase pas une saisie en
cours.

Le champ du site accepte 250 caractères ; un nom de zone et un titre de quête
tiennent très en deçà, mais le repère est borné quand même, pour qu'une saisie ne
parte jamais tronquée par le navigateur.

### Le pont ne sert pas ici, il gêne

Le script de cadrage masque le pied d'article. Le signalement a donc le sien, qui
fait l'inverse : il déplie le `details`, remonte du bloc jusqu'au corps en
masquant à chaque étage tout ce qui n'est pas sur le chemin, et ne laisse que le
formulaire. **Masquer plutôt que retirer** : le nœud reste où le site l'a mis,
avec son jeton de sécurité et son champ de provenance, et rien de ce que le site
attend autour n'est cassé.

Trois défauts vus à l'écran, tous de mise en page : le fond photographique du
site passait derrière les champs, les champs débordaient par la droite parce
qu'ils sont taillés pour une colonne d'article, et deux barres de défilement
paraissaient côte à côte. Le style injecté règle les trois.

Le script rend « pret » ou « absent », et l'état est journalisé : une page sans
formulaire, ou un pied d'article que le site aurait changé, s'ouvre alors telle
quelle, et la fenêtre reprend un titre qui ne promet plus ce qu'elle ne montre
pas.

### Le bouton se voit, l'accès au navigateur passe en icône

Le pied de la fenêtre des guides porte le crédit du site, qu'on doit pouvoir
lire : c'est le nom de ceux dont on affiche le travail. Trois libellés écrits le
réduisaient à « Guide… » dans une fenêtre de 570 pixels.

Le signalement a d'abord été mis en drapeau seul, et il s'y confondait avec le
reste. Il porte maintenant son libellé et les couleurs de l'accent **au repos**,
non au seul survol : c'est le geste qu'on cherche dans cette barre. L'accès au
navigateur, lui, passe en icône : il est secondaire, et son infobulle le dit.

### Reprise : où le formulaire existe, et ce qu'on fait quand il n'existe pas

Relevé page par page plutôt que supposé :

| Page | Formulaire |
|:--|:--|
| Guide de quête, de donjon, de chemin, page de zone | oui |
| Accueil, « /quetes/ », « /donjons/ », « /raids/ », « /tanieres/ » | non |
| « /contact/ », « /mentions-legales/ » | non |

Le site n'a **pas de formulaire général** : sa page de contact renvoie vers le
serveur Discord de l'équipe. C'est donc elle le repli, et la fenêtre y descend
sur le texte, sa bannière occupant sinon la moitié d'une fenêtre étroite.

Le repli se décide à deux endroits. La vue-modèle sait déjà si la fenêtre montre
une rubrique ou un article : depuis une rubrique, on va droit au contact sans
ouvrir une page pour y constater l'absence. Et le script rend « absent » quand le
pied d'article n'a rien, ce qui rattrape un article sans formulaire ou un site
qui aurait changé.

### Le bouton d'envoi du site est invisible, et c'est chez eux

Sa feuille de style dit :

```css
.papycha-report__submit { background: currentColor; color: Canvas; }
```

`currentColor` vaut la couleur du texte de l'élément lui-même, c'est-à-dire
`Canvas` : le fond et le texte prennent donc la même couleur, et le bouton
disparaît. On lui rend les deux couleurs qu'il visait, dans le même vocabulaire
de couleurs système pour qu'il suive le thème clair ou sombre.

C'est une correction portée sur la page d'autrui, ce qu'on ne fait pas à la
légère. Elle se justifie ici : sans elle, la fenêtre montre un formulaire qu'on
ne peut pas envoyer.

### La zone de texte ne s'étire plus

La poignée de redimensionnement est retirée et la hauteur exprimée en unités de
fenêtre, entre deux bornes : le formulaire défile, il ne se redimensionne pas, et
il tient aussi bien sur un portable que sur un grand écran. Haute comme le site
la donne, la zone poussait le bouton d'envoi sous le bord inférieur.

## D75 - Choisir une étape, et non seulement avancer d'une

Le rang de l'étape n'était qu'un texte. Les deux flèches avançaient d'une étape
à la fois : sur un guide de treize étapes, revenir à la troisième demandait neuf
clics, et rien ne disait ce qu'on trouverait en chemin.

Le rang déplie donc la liste des étapes, chacune avec son numéro et son résumé,
celle où l'on est marquée. Le choix passe par `GoToStep`, déjà écrit pour les
flèches : la page se replace, et le bandeau suit.

**Un seul endroit pose les étapes.** Quatre chemins remettaient la liste à zéro
en écrivant chacun les deux mêmes lignes ; une cinquième ligne à tenir les aurait
fait diverger. `ResetSteps` les réunit, et le résumé d'une étape est écrit une
fois pour le bandeau et pour la liste, y compris la réserve du départ, dont la
première étape emprunte les métadonnées de la quête.


## D76 - Ce qui est compté comme étape doit en être

Le découpage en étapes avait été réglé sur des échantillons de seize à
cinquante-cinq guides. Le site en compte 782, et rien ne disait ce que la règle
attrapait ailleurs.

### L'audit passe par l'interface du site, pas par ses pages

`posts?categories=7&per_page=100&_fields=link,content` rend le contenu rendu de
cent articles par requête : **les 782 guides en huit requêtes**, là où les pages
complètes en auraient coûté 782 sur le site d'un bénévole. Et `content.rendered`
est exactement ce que la règle parcourt, l'intérieur de `.entry-content`,
bandeau d'intro compris. Vérifié avant de s'y fier.

Relevé de départ : 777 guides en mode paragraphe, 5 en mode sections, **3 293
étapes-paragraphes**, 3 guides sans aucune étape.

### L'encart n'était pas compté, il était affiché

C'est un `dl.pqa-quest-intro__taxonomies`, petit-enfant du contenu ; la règle
n'examine que les enfants directs de balise `p`. Il n'a donc jamais été une
étape.

Mais l'étape de départ est ancrée en haut du contenu, et ce bloc était **le seul
morceau du bandeau d'intro que la fenêtre des quêtes ne masquait pas** :
`__facts` et `__start` l'étaient pour les deux fenêtres, `__requirements` pour
celle des quêtes, et la taxonomie avait été oubliée. C'est donc elle qu'on voyait
en étape 1.

Le bandeau y passe maintenant en entier, `section.pqa-quest-intro`, et non enfant
par enfant : relevé sur cent guides, la section n'a que quatre enfants directs
possibles et trois étaient déjà masqués. Une liste d'enfants laisserait passer le
prochain que le site ajoutera ; la mesure en a d'ailleurs trouvé un cinquième,
`__rewards`, sur un guide.

Masquer ne retire rien du document : le pont continue de lire le bloc de départ
et de poster le bandeau à la fenêtre.

### Trois familles retirées, trois écartées

Chaque règle candidate a été mesurée sur les 782 guides, et **chaque étape
qu'elle retirait a été relue une à une**.

| Règle | Retire | Verdict |
|:--|--:|:--|
| Le garde des sujets vaut aussi pour les irréguliers | 7 | retenue, sept récits |
| L'annonce du départ sans consigne propre | 30 | retenue, trente redites du bandeau |
| L'encart « Important : » | 4 | retenue, même famille que « Attention : » |
| Les pronoms objets gardent aussi | +31 | **écartée** |
| Refuser une puce ou une minuscule en tête | 2 | **écartée** |
| Retirer la branche « couleur » | 3 | **écartée** |

Les trois retenues retirent 41 étapes sur 3 293, en ajoutent zéro, et laissent
le nombre de guides sans étape inchangé.

**Les trois écartées le sont pour une seule raison : elles perdaient de vraies
consignes.** Les pronoms objets tuaient « Badufron emmène le Sadida avec lui,
parlez avec Raymond Santho », où « parlez » suit « lui ». La minuscule en tête
tuait « on continue par le drapeau de Korhog cette fois-ci en [-54,34] », qui est
bien une étape. La branche « couleur » ne rapportait que trois étapes, dont une
vraie consigne.

**L'échantillon avait donné deux de ces trois pour bonnes.** Sur soixante-trois
guides, la puce et la couleur paraissaient nettes ; sur 782, elles se retournent.
C'est ce qui justifie l'audit complet plutôt qu'un sondage.

### Ce que la sonde retient

Deux ajouts à `build/sonde-papycha` :

- **Le bandeau d'intro existe encore.** Le jour où le site le renomme, le
  masquage devient muet et l'encart reparaît en tête de guide, à l'endroit exact
  où la première étape est ancrée. Mesuré à 94 guides sur 100 ; les six autres
  n'ont pas de bandeau du tout.
- **L'annonce du départ se tourne encore ainsi.** Quatre-vingt-sept paragraphes
  sur cent guides emploient la formule que le pont écarte. Si le site la tourne
  autrement, le nombre s'effondre et l'exclusion ne mord plus.

### Le vrai défaut de volume est ailleurs, et il n'est pas corrigé ici

`isObjective` exige du gras ou une couleur. **Cent cinquante-trois guides sur 777
ne rendent aucune étape-paragraphe**, non qu'ils n'aient pas de consignes, mais
parce que leurs auteurs n'emploient jamais le gras. L'ordre de grandeur mesuré
sur un échantillon est d'une centaine de consignes perdues, contre quarante et
une fausses retirées ici.

Le gras n'est pas une convention du site, c'est une habitude d'auteur. Corriger
cela demande une autre règle et un autre audit ; ce n'était pas la demande, et
c'est signalé plutôt que fait à la sauvette.


## D77 - Sept reprises, dont deux causes qu'il fallait aller chercher

### Le soulignement venait du thème du site

`stargazer/style.css` porte `label:focus { text-decoration: underline }`. Les
champs du formulaire de signalement sont écrits
`<label><span>intitulé</span><input></label>` : cliquer dans un champ met le
libellé au focus, et **la décoration se propage à tout ce que la boîte
contient**. D'où l'intitulé et la valeur soulignés.

On la coupe **sur le libellé lui-même**, et non sur ses descendants : une
décoration héritée par propagation ne s'annule pas depuis l'enfant, il faut
l'empêcher à sa source. Le `<summary>` garde la sienne, qui dit qu'on peut le
toucher.

### Masquer un « summary », c'est supprimer le seul moyen d'ouvrir

Le formulaire du site vit dans un `<details>` dont le `<summary>` dit « Remonter
une erreur ». Je l'avais masqué en le prenant pour un doublon du titre de la
fenêtre. Il n'en était pas un : c'était la poignée du bloc.

**Un élément qui a l'air décoratif peut être un geste.** Il est de retour,
ramené à gauche, le site l'alignant à droite pour terminer une ligne de
métadonnées.

### Une bulle liée dans un seul sens ne se rouvre qu'au deuxième clic

`IsOpen="{Binding IsChecked, ElementName=Bascule}"` sans `Mode=TwoWay` : quand la
bulle se referme d'elle-même, au clic ailleurs, la bascule reste cochée. Le clic
suivant ne fait que la décocher, et il en faut deux pour rouvrir.

Les trois bulles du projet avaient le défaut, écrites sur le même modèle : les
étapes, les profils, les réglages fins. **Un défaut de gabarit se recopie avec
le gabarit** ; c'est là qu'il faut chercher les autres.

### La rubrique qu'on parcourt l'emporte sur celle du catalogue

`QuestSummary` garde toutes les rubriques d'une quête dans `SectionIds`, mais
n'en expose qu'une dans `SectionId`, choisie comme **la moins peuplée**
(`QuestCatalogService`). `OpenList` s'en servait pour rouvrir la liste : parti de
Frigost sur une quête aussi principale, on rouvrait sur « Quêtes principales ».

Mesuré sur le cache du catalogue, 782 quêtes indexées :

| | |
|:--|--:|
| quêtes à plus d'une rubrique | 215 |
| dont une rubrique transverse emporte la zone | 93 |
| dont la zone emporte la transverse | 80 |
| « Quêtes répétables » : quêtes partagées | 113 / 128 |

`_section` retenait déjà la rubrique ouverte et survivait à la lecture d'un
guide ; il n'était écrasé que là. La règle tient en une ligne, `SectionSeen` :
**la rubrique qu'on parcourt si la quête y figure, sinon celle du catalogue.**
L'étiquette de série des liens précédente et suivante la suit, pour la même
raison : deux quêtes de la même zone s'y annonçaient d'une série différente parce
que l'une était aussi répétable.

**Ce qui reste**, relevé et laissé : deux quêtes portent le titre
« L'Ascension », et l'index des chaînes retient la première. Une collision sur
782, sur un chemin qui ne dévie pas la navigation.

### Le reste

Le bouton de signalement ne paraît que sur un guide : le site ne met de
formulaire qu'en pied d'article et n'en a pas de général, donc le repli vers la
page de contact disparaît avec `PapychaSite.ContactUrl`.

La fenêtre de signalement épouse le formulaire. Le script rend la hauteur du
bloc, la fenêtre s'y pose, bornée à l'écran ; sa largeur est figée par
`MinWidth` et `MaxWidth`, et le style `WS_MAXIMIZEBOX` est retiré à la main, WPF
ne sachant pas ôter le seul agrandissement sans figer aussi la hauteur.

L'icône des quêtes prend la teinte neutre des trois autres lignes de l'accueil :
elle y était la seule en couleur, alors que les quatre natures s'y valent.
`GlyphQuestBrush` n'ayant plus d'emploi, la palette passe de cinq teintes à
quatre.


## D78 - Un lien s'ouvre sur place, donc il s'empile et garde le fil

### La règle, telle qu'elle se dit

**Un lien s'ouvre à part quand le catalogue ne le connaît pas, et sur place
quand il le connaît. Dès qu'il s'ouvre sur place, la flèche de retour paraît, et
rouvrir le panneau montre la fiche d'où l'on vient.**

Une seule règle, sans exception de nature. Le chemin n'est que le cas par lequel
le défaut s'est vu.

### Ce qui n'allait pas

Relevé sur « Les chasses de Crocodaille Dandi », dont l'étape 1 renvoie vers
`chemin-du-zaap-du-village-de-la-canopee-otomai`, une page du catalogue :

- `TryFollowUrl` n'empilait l'historique **que dans la branche des quêtes**. Les
  branches donjon et chemin sortaient avant, et la flèche ne paraissait jamais.
- `_visited` était un `Stack<QuestSummary>` : il **ne pouvait pas** retenir un
  chemin. Il n'était jamais vidé non plus, si bien que la flèche survivait à un
  choix dans la liste en pointant une page sans rapport.
- `SetCurrent(PathSummary)` met `_current` à `null`, et `OpenList` ne savait
  déduire une rubrique que de la page courante : le panneau rouvrait sur la
  branche des chemins, et plus rien ne disait d'où l'on venait.

### Ce qui a changé

**L'empilement remonte avant l'aiguillage.** Une seule condition, en tête de
`TryFollowUrl`, et les quatre natures en héritent par construction plutôt que
par une liste de cas à tenir à jour. L'historique retient des **adresses**, et
le retour repasse par la même porte que l'aller : `TryFollowUrl` décide de la
nature dans les deux sens.

Seule une adresse que le catalogue sait rouvrir est empilée. Sans cette réserve,
la flèche aurait pu pointer une page de repli qu'elle n'aurait pas su rouvrir.

**Le repère du panneau se distingue de la page courante.** Deux champs,
`_anchorSection` et `_anchorUrl`, que les trois surcharges de `SetCurrent`
posent, **sauf quand on suit un lien**. La distinction n'a pas eu à être
inventée : `remember` vaut vrai exactement pour un lien suivi dans la page, et
`SetCurrent` reçoit `anchor: !remember`.

C'est ce qui permet d'aller voir un chemin et de revenir : on regarde ailleurs,
le repère ne bouge pas.

**Choisir dans la liste efface la piste.** On a désigné où aller ; ce qu'on
lisait avant ne veut plus rien dire.

### Deux voisins trouvés en tirant le fil

Le repli de `Follow`, quand la page n'est pas au catalogue, affichait sans
toucher aux trois champs d'état : `_current` mentait, et tout ce qui s'en sert
avec lui. Ils sont vidés.

`UrlKey` ne retirait pas le fragment là où le routage de la fenêtre le retire :
une adresse du catalogue ornée d'une ancre partait en fenêtre annexe. Le
fragment part, **la chaîne de requête reste** : au moins une adresse du
catalogue en fait son identité.

### Ce qui reste

Les pages de rubrique partent toujours en fenêtre annexe : leurs adresses de
lien, `/category/zones/…`, n'ont pas la forme de celles que le catalogue retient,
`/quetes/…`. Les rapprocher demanderait un autre travail.

Rien de tout cela n'a d'épreuve automatique : la vue-modèle des guides n'en a
aucune aujourd'hui, et lui en donner demanderait un faux catalogue. Tout a été
vérifié à l'écran, sur le cas signalé et sur son retour.


## D79 - L'ordre des comptes est un réglage général, pas un instantané

Le rangement des onglets à la souris se persistait déjà : le glissement appelle
`MoveInstanceAsync`, et le champ `Order` du document en garde la trace. Il ne
survivait pourtant pas au démarrage suivant.

**La cause était une décision que je venais de prendre.** En rendant le profil
plus complet, j'avais fait qu'ouvrir un profil rejoue l'ordre qu'il avait
retenu. Or un profil de démarrage s'ouvre tout seul : chaque lancement défaisait
donc le rangement de la veille. Mesuré sur le fichier de réglages, l'ordre
revenait à celui du profil « Duo haute » à chaque démarrage.

**L'ordre est un réglage général**, celui de la liste des comptes comme celui des
onglets, et les deux sont le même. Le profil le retient dans `InstanceKeys`, pour
que le fichier se lise, mais ne l'impose pas. C'est la seule façon que la demande
tienne dans ses trois branches à la fois : garder l'ordre pour la session, pour
les profils, et d'un lancement à l'autre.

Le contraste avec les positions de fenêtres est assumé : une position est
attachée à une disposition d'écran, qu'on veut retrouver telle quelle en ouvrant
un profil. Un ordre de lecture, non : c'est une habitude, et elle ne change pas
selon le profil qu'on ouvre.

**Les onglets sont reposés dans cet ordre à chaque arrivée**, et non laissés dans
celui des arrivées. Les afficheurs ne se préparent pas à la même vitesse, et rien
ne garantissait que l'ordre d'ouverture soit celui du rangement. La garantie est
maintenant explicite plutôt que constatée.
