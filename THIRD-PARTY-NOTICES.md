# Composants tiers

DT Hub est publié sous licence MIT. Il s'appuie sur des composants tiers qui
conservent leur propre licence. Ce fichier recense ces composants, la manière
dont ils arrivent chez l'utilisateur et les obligations qui en découlent.

Aucune notice de copyright ni aucun texte de licence tiers ne doit être retiré.

## Vue d'ensemble

| Composant | Licence | Mode de distribution |
|---|---|---|
| scrcpy | Apache License 2.0 | Téléchargé depuis GitHub au premier lancement |
| Android SDK Platform Tools (adb) | Android SDK License Agreement | Téléchargé depuis Google au premier lancement |
| .NET runtime | MIT | Inclus par la publication self-contained |
| CommunityToolkit.Mvvm | MIT | Paquet NuGet |
| Serilog et ses puits | Apache License 2.0 | Paquets NuGet |
| Microsoft.Extensions.* | MIT | Paquets NuGet |
| Microsoft.Web.WebView2 | Licence Microsoft, non libre | Paquet NuGet, moteur fourni par Windows |
| Données de quêtes de papycha.fr | Non libre, voir plus bas | Fichier embarqué dans l'exécutable |
| xUnit, coverlet | Apache License 2.0, MIT | Dépendances de test, non distribuées |

## scrcpy

- Projet : https://github.com/Genymobile/scrcpy
- Copyright : Copyright (C) 2018 Genymobile, Copyright (C) 2018-2026 Romain Vimont
- Licence : Apache License 2.0

La licence Apache 2.0 autorise la redistribution. DT Hub télécharge tout de
même scrcpy chez l'utilisateur, depuis l'archive officielle publiée par le
projet sur GitHub, par le même mécanisme vérifié que pour ADB. Deux raisons :
l'installateur reste léger, et il n'y a qu'un seul chemin de mise en place à
maintenir et à tester.

Les obligations correspondantes sont remplies ainsi :

- l'archive amont contient son propre `LICENSE.txt`, extrait tel quel et
  conservé à côté de l'exécutable ;
- DT Hub ne modifie aucun fichier de scrcpy. Le fonctionnement retenu pour
  lancer une application sur un utilisateur Android secondaire n'exige aucune
  modification, comme expliqué dans `docs/DECISIONS.md` et
  `third_party/scrcpy/MODIFICATIONS.md` ;
- si une modification devenait nécessaire, elle serait signalée dans ce même
  fichier, accompagnée du patch reproductible.

L'archive Windows de scrcpy contient elle-même une copie d'`adb.exe`. DT Hub
ne s'en sert pas : il utilise la sienne, obtenue directement chez Google, dont
il maîtrise la version. Le chemin lui en est indiqué par la variable
d'environnement `ADB`, que scrcpy honore.

DT Hub n'est pas affilié à Genymobile ni aux auteurs de scrcpy et n'utilise
pas leur nom ni leurs logos comme élément de sa propre identité.

## Android SDK Platform Tools (adb)

- Éditeur : Google LLC
- Licence : Android Software Development Kit License Agreement
- Source officielle, versionnée et donc au contenu immuable :
  https://dl.google.com/android/repository/platform-tools_r37.0.1-win.zip

  L'adresse « latest » n'est pas employée : son contenu change sans prévenir,
  et l'empreinte déclarée dans `build/dependencies.json` ne vaudrait plus rien.

Le contrat de licence du SDK Android n'autorise pas la redistribution des
binaires. Les platform tools ne sont donc **pas** inclus dans le dépôt ni dans
l'installateur. DT Hub les télécharge depuis l'URL officielle ci-dessus, au
premier lancement, dans le dossier de données de l'utilisateur, et vérifie
l'archive avant de l'extraire.

L'utilisateur en est informé : au premier lancement, une fenêtre nomme chaque
composant, sa version et l'adresse d'où il vient, et montre l'avancement du
téléchargement puis de la vérification. Aucune autre source n'est utilisée.

## .NET

- Éditeur : Microsoft Corporation
- Licence : MIT
- https://github.com/dotnet/runtime

DT Hub est publié en self-contained : le runtime .NET est inclus dans
l'application, ce que la licence MIT autorise.

## CommunityToolkit.Mvvm

- Projet : https://github.com/CommunityToolkit/dotnet
- Licence : MIT

## Serilog

- Projet : https://github.com/serilog/serilog
- Licence : Apache License 2.0
- Paquets employés : `Serilog.Extensions.Hosting`, `Serilog.Sinks.File`,
  `Serilog.Sinks.Debug`

Journalisation de l'application. Distribué avec l'exécutable.

## Microsoft.Extensions.Hosting et Microsoft.Extensions.Logging.Abstractions

- Projet : https://github.com/dotnet/runtime
- Licence : MIT

Hôte générique et abstractions de journalisation, du même dépôt que le runtime
.NET. Distribués avec l'exécutable.

## Microsoft.Web.WebView2

- Éditeur : Microsoft Corporation
- Projet : https://developer.microsoft.com/microsoft-edge/webview2/
- Licence : conditions de distribution Microsoft, **qui ne sont pas une licence
  libre**. Voir le fichier de licence livré avec le paquet NuGet.

C'est la seule dépendance de ce type. Deux choses distinctes en découlent : le
paquet NuGet, qui n'apporte que l'amorce et les liaisons managées, est distribué
avec l'exécutable ; le moteur de rendu lui-même n'est **pas** distribué par
DT Hub, il est fourni avec Windows 11 et arrive sur Windows 10 par Microsoft
Edge. Son absence est détectée et dite à l'utilisateur, elle n'empêche que les
fenêtres de guides.

## Données de quêtes issues de papycha.fr

- Source : https://papycha.fr
- Fichier : `assets/quest-successes.json`, embarqué dans l'exécutable
- Relevé le 2026-08-30 par `build/extract-successes.py`

**Ce fichier n'est pas couvert par la licence MIT de DT Hub.** Il contient 719
entrées indexées par adresse de page, portant le nom du succès auquel une quête
appartient, son rang, et les titres des quêtes prérequises. Ce sont des titres
d'œuvre du jeu DOFUS Touch, et surtout une structure de progression que
papycha.fr a établie par un travail éditorial qui lui appartient.

Aucun texte de quête, aucune description, aucune solution n'est repris : le
fichier ne sert qu'à savoir dans quel ordre lire les pages du site, et
l'application renvoie toujours au site pour le contenu lui-même.

DT Hub n'est affilié ni à papycha.fr ni à Ankama. Quiconque réutilise ce dépôt
sous licence MIT doit traiter ce fichier à part et s'adresser à papycha.fr.

## xUnit

- Projet : https://github.com/xunit/xunit
- Licence : Apache License 2.0

Dépendance de test uniquement, absente de l'application distribuée.

## Marques et contenus des applications mirrorées

Les icônes et les noms des applications installées sur le téléphone sont lus
sur l'appareil et affichés localement dans le sélecteur, uniquement pour
permettre à l'utilisateur de reconnaître ses propres applications. Ils ne sont
ni redistribués, ni stockés hors de la machine de l'utilisateur, ni utilisés
comme éléments de communication de DT Hub.

DT Hub n'embarque aucun logo ni aucune ressource graphique appartenant à un
éditeur d'application tiers. Les deux seules images du dépôt, `assets/app.png`
et `assets/app.ico`, sont dessinées par `build/make-icon.py`.

Deux exceptions, nommées ici parce qu'une règle qui ne dit pas ses exceptions ne
protège plus rien : les titres de quêtes du fichier décrit plus haut, et les
captures d'écran du README, qui montrent l'application en fonctionnement et donc
le jeu qu'elle affiche. Les unes servent l'interopérabilité, les autres
illustrent la documentation.

## Ajouter une dépendance

Avant d'ajouter un composant tiers :

1. vérifier que sa licence autorise l'usage envisagé, redistribution comprise ;
2. si la redistribution n'est pas autorisée, préférer un téléchargement depuis
   la source officielle, avec vérification ;
3. ajouter une entrée dans ce fichier, avec le lien, la licence et le mode de
   distribution ;
4. conserver les fichiers de licence et de notice fournis en amont.
