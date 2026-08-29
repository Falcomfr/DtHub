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
