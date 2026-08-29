# État d'avancement

Légende : **DONE** terminé et vérifié, **IN PROGRESS** en cours,
**TODO** pas commencé, **BLOCKED** nécessite une action externe.

Dernière mise à jour : 2026-08-29

## Vérifié sur matériel réel

Xiaomi 13T, Android 16, profil principal « Alice Martin » et profil cloné
« XSpace » (999), DOFUS Touch installé sur les deux.

| Élément | État |
|---|---|
| Téléchargement et vérification d'ADB depuis Google | DONE |
| Détection du téléphone en Wi-Fi, y compris sous son nom mDNS | DONE |
| Lecture des profils Android et classement des types | DONE |
| Détection du jeu sur les deux profils, activité résolue | DONE |
| Reconnexion automatique après coupure | DONE |
| Affichage des instances dans la fenêtre de mise en route | DONE |
| Ouverture effective des deux fenêtres de jeu | DONE |
| Jeu affiché en plein écran virtuel, sans bande noire | DONE |
| Superposition exacte, même position et même taille | DONE |
| Ancrage du bloc de jeu et placement du configurateur | DONE |
| `Ctrl+Tab` et `Ctrl+P` en conditions réelles | TODO |

## Noyau

| Élément | État |
|---|---|
| Exécution de processus sans console, bornée, annulable | DONE |
| Processus durable avec lecture de sortie au fil de l'eau | DONE |
| Client ADB, erreurs traduites en messages actionnables | DONE |
| Parseurs `devices`, `getprop`, `mdns`, `pair`, `connect` | DONE |
| Manifeste de dépendances vérifiées, ADB et scrcpy | DONE |
| Persistance JSON tolérante à la corruption | DONE |
| Découverte et mémorisation des téléphones | DONE |
| Appairage Wi-Fi assisté, code jamais journalisé | DONE |
| Reconnexion automatique en trois temps | DONE |
| Profils Android, aucun identifiant supposé | DONE |
| Découverte des instances du jeu par profil | DONE |
| Lancement par ADB sur n'importe quel profil | DONE |
| Sessions scrcpy indépendantes, afficheur virtuel par session | DONE |
| Empilement des fenêtres, grille de neuf positions | DONE |
| Raccourcis, validation, conflits, enregistrement Win32 | DONE |
| Réglages, fusion des instances mémorisées | DONE |

## Interface

| Élément | État |
|---|---|
| Fenêtre de mise en route, surveillance continue | DONE |
| Panneau d'association Wi-Fi, pré-remplissage réseau | DONE |
| Configurateur flottant, `Ctrl+P`, coin libre | DONE |
| Onglet Général : position, taille, écran | DONE |
| Onglet Appareils : état, coche, nom, relance | DONE |
| Onglet Raccourcis : édition, conflits, restauration | DONE |
| Thème suivant Windows, clair et sombre | DONE |
| Journalisation avec rotation, dossier accessible | DONE |

## Distribution

| Élément | État |
|---|---|
| Publication en fichier unique, 60 Mo, sans installateur | DONE |
| Décision : pas d'installateur ni de Velopack | DONE |
| Signature Authenticode | TODO (non nécessaire pour un usage personnel) |
| Dépôt GitHub public | TODO |
| GitHub Actions build et tests | TODO |

## Reste à faire

| Élément | État |
|---|---|
| Essai complet du lancement des deux comptes | IN PROGRESS |
| Options supplémentaires dans l'onglet Général | TODO (à définir) |
| Réglages de mirroring exposés dans l'interface | TODO |
