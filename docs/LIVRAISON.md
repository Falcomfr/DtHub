# Livrer une version

DT Hub se met à jour depuis les livraisons du dépôt. L'application demande la
dernière au démarrage, la télécharge en fond si la case « Se mettre à jour toute
seule » est cochée, vérifie son empreinte, et pose le nouvel exécutable quand on
quitte. La note de version paraît au démarrage suivant, celui qui exécute enfin
la nouvelle version.

## Ce qu'il faut une seule fois

Le dépôt doit être **public** : l'application interroge l'API sans jeton, et un
jeton posé dans l'exécutable serait lisible par qui l'ouvre. Son compte et son
nom sont écrits dans `src/DtHub.Core/Updates/ReleaseChannel.cs`.

Tant que le dépôt n'existe pas, la demande rend « rien à signaler » et
l'application n'en sait pas plus : rien ne casse, rien ne s'affiche.

## Livrer

1. Porter la version dans `Directory.Build.props`, champ `VersionPrefix`.
2. Fermer la section `## [Non publié]` du journal : la renommer
   `## [0.2.0] - 2026-09-02`. La chaîne de livraison y lit la note de version,
   et refuse de livrer si elle ne l'y trouve pas.
3. Valider les deux, puis étiqueter avec le même numéro :

   ```
   git tag v0.2.0
   git push origin main --tags
   ```

La chaîne éprouve, publie, calcule l'empreinte et crée la livraison avec deux
fichiers : `DtHub.exe` et `DtHub.exe.sha256`. Ces deux noms sont ceux que
l'application attend ; une livraison à laquelle il en manque un est ignorée.

## Ce que l'application fait de tout cela

**Elle ne se met pas à jour depuis un arbre de sources.** Si le fichier de
solution se trouve au-dessus de l'exécutable, la mise à jour est refusée : le
lanceur de développement republie à chaque démarrage et l'écraserait dans la
seconde, en faisant croire à une régression.

**Elle vérifie avant de poser.** L'empreinte du fichier téléchargé est comparée
à celle que la livraison annonce. Un écart, et le fichier est effacé sans avoir
servi.

**Elle ne se remplace jamais en pleine session.** Un exécutable qui tourne ne
peut pas être écrasé, mais il peut être renommé : à l'arrêt, l'ancien s'écarte
en `DtHub.exe.ancien`, le nouveau prend sa place, et le démarrage suivant
balaie ce qui reste. Si la seconde moitié échoue, la première est défaite.

**Rien de tout cela n'est une panne.** Pas de réseau, dépôt absent, quota
atteint, empreinte fausse, fichier verrouillé : l'application continue avec la
version qu'elle a.

## Vérifier une livraison à la main

```
gh release view v0.2.0
sha256sum DtHub.exe
```

L'empreinte affichée doit être celle du fichier `.sha256` de la livraison.
