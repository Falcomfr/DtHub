# Modifications apportées à scrcpy

**État au 2026-08-29, scrcpy v4.1 : aucune modification.**

DT Hub utilise scrcpy tel qu'il est publié par le projet amont. L'archive
officielle est téléchargée, son empreinte vérifiée, et extraite sans altérer
un seul fichier. Le `LICENSE.txt` fourni en amont est conservé à côté de
l'exécutable.

Ce fichier existe pour deux raisons : documenter cette absence de
modification, et fixer la procédure à suivre si elle devenait nécessaire, la
licence Apache 2.0 imposant de signaler tout fichier modifié.

## Pourquoi aucune modification n'est nécessaire

Le besoin qui aurait pu l'exiger est de lancer une application sur un
utilisateur Android autre que le principal : clone, profil géré, second
espace. L'option `--start-app` de scrcpy ne prend pas d'identifiant
d'utilisateur, et la faire évoluer demanderait un fork du serveur Java, donc
une chaîne de compilation Android et un suivi permanent de l'amont.

Ce n'est pas nécessaire, car scrcpy sait créer un afficheur virtuel et publie
son identifiant. Dans `NewDisplayCapture.java`, à la création de l'afficheur :

```java
Ln.i("New display: " + width + "x" + height + "/" + dpi + " (id=" + virtualDisplayId + ")");
```

Cette ligne est relayée au client, donc lisible sur la sortie du processus.

DT Hub procède donc ainsi :

1. `scrcpy --new-display=<taille>/<densité> --no-vd-system-decorations` ;
2. lecture de l'identifiant d'afficheur dans la sortie de scrcpy ;
3. `adb shell am start --user <identifiant> --display <afficheur> -n <composant>`.

Le lancement de l'application est fait par ADB, qui accepte `--user` depuis
toujours. N'importe quel identifiant d'utilisateur entier fonctionne, sans
valeur particulière supposée.

Cette voie ne touche ni à scrcpy, ni à l'application Android ciblée.

## Si une modification devenait nécessaire

À faire, dans cet ordre :

1. décrire ici le besoin et pourquoi aucune autre voie ne convient ;
2. produire un patch reproductible et le déposer dans ce dossier, sous la
   forme `NNNN-description.patch`, applicable sur un tag amont précis ;
3. indiquer ici le tag amont concerné, la commande d'application, et la
   procédure de reconstruction ;
4. signaler les fichiers modifiés en tête de ceux-ci, comme l'exige la section
   4b de la licence Apache 2.0 ;
5. mettre à jour `THIRD-PARTY-NOTICES.md` et `docs/DECISIONS.md`.

Sans ces cinq points, la modification ne doit pas être fusionnée.
