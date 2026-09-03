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
| xUnit | Apache License 2.0 | Dépendance de test, non distribuée |

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

DT Hub n'embarque aucun logo ni aucune ressource appartenant à un éditeur
d'application tiers.

## Ajouter une dépendance

Avant d'ajouter un composant tiers :

1. vérifier que sa licence autorise l'usage envisagé, redistribution comprise ;
2. si la redistribution n'est pas autorisée, préférer un téléchargement depuis
   la source officielle, avec vérification ;
3. ajouter une entrée dans ce fichier, avec le lien, la licence et le mode de
   distribution ;
4. conserver les fichiers de licence et de notice fournis en amont.
