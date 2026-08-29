# État d'avancement

Légende : **DONE** terminé et vérifié, **IN PROGRESS** en cours,
**TODO** pas commencé, **BLOCKED** nécessite une action externe.

Dernière mise à jour : 2026-08-29

## Phase 1 - Prérequis, solution, build vide

| Élément | État | Note |
|---|---|---|
| Vérification .NET / Git / winget / gh | DONE | .NET 10.0.400 installé via winget |
| Solution + 4 projets + références | DONE | `DtHub.slnx` |
| Identité produit centralisée | DONE | `Directory.Build.props` + `ProductInfo.cs` |
| Icône applicative | DONE | `build/make-icon.py` |
| `.editorconfig` / `.gitattributes` | DONE | |
| Build vert | DONE | 0 avertissement, 0 erreur |
| Tests verts | DONE | 2 tests |
| Git initialisé + premier commit | DONE | |
| AGENTS.md / README.md | DONE | + DECISIONS.md, ROADMAP.md, notices tierces |

## Phase 2 - Exécution de processus et ADB

| Élément | État |
|---|---|
| `IProcessRunner` + implémentation | DONE |
| `AdbClient` (start, devices, shell, timeout, annulation) | DONE |
| Parseurs `adb devices` / `devices -l` / `getprop` | DONE |
| Classification et traduction des erreurs ADB | DONE |
| Manifeste de dépendances + téléchargement vérifié d'ADB | DONE |
| `AppPaths` sous `%LOCALAPPDATA%` | DONE |
| Tests (76 au total, aucun ne requiert de téléphone) | DONE |

## Phase 3 - Découverte des appareils

| Élément | État |
|---|---|
| Modèle `AndroidDevice` avec identité stable USB / Wi-Fi | DONE |
| `DeviceDiscoveryService` (cache, déduplication, hors ligne) | DONE |
| `getprop` avec replis par constructeur | DONE |
| `DeviceRegistry` sur `devices.json` | DONE |
| Persistance JSON tolérante à la corruption | DONE |

## Phase 4 - Appairage Wi-Fi

| Élément | État |
|---|---|
| `DevicePairingService` (`adb pair` puis connexion) | DONE |
| Découverte mDNS (`adb mdns services`) | DONE |
| Reconnexion automatique en trois temps | DONE |
| Code d'appairage masqué dans les journaux | DONE |
| Repli sur saisie manuelle du port si le mDNS est bloqué | DONE |

## Phase 5 - Utilisateurs Android

| Élément | État |
|---|---|
| `AndroidUserService` (`pm list users`) | DONE |
| Support de tout `userId` entier, aucune valeur câblée | DONE |
| Affinage des types par `dumpsys user` | DONE |
| Démarrage d'un profil arrêté | DONE |
| Repli sur l'utilisateur principal si la commande échoue | DONE |

## Phase 6 - Applications

| Élément | État |
|---|---|
| `AppDiscoveryService` par appareil et par profil | DONE |
| Repli paquet par paquet si l'interrogation groupée manque | DONE |
| Vrais noms via scrcpy, repli lisible sur le paquet | DONE |
| Marquage des applications système | DONE |
| Cache disque du catalogue | DONE |
| Icônes réelles des applications | BLOCKED (voir docs/DECISIONS.md, D6) |
| Favoris, recherche, filtres | TODO (portés par les réglages, phase 11) |

## Phase 7 - Profils de lancement

| Élément | État |
|---|---|
| `LaunchProfile` / `LaunchTarget` | DONE |
| `ProfileService` : créer, renommer, dupliquer, supprimer | DONE |
| Ordre des sessions modifiable | DONE |
| Profil par défaut et dernier utilisé | DONE |
| Nettoyage des sessions d'un appareil oublié | DONE |

## Phase 8 - scrcpy

| Élément | État |
|---|---|
| Options scrcpy et construction de la ligne de commande | DONE |
| Processus durable avec lecture de sortie au fil de l'eau | DONE |
| `ScrcpySessionManager`, sessions indépendantes | DONE |
| Lancement sur n'importe quel profil Android | DONE |
| Revalidation du composant après mise à jour d'application | DONE |
| Vrais noms d'applications via `scrcpy --list-apps` | DONE |
| Aucune modification de scrcpy, procédure documentée | DONE |
| Vérification sur matériel réel | BLOCKED (phase 13) |

## Phase 9 - Fenêtres

| Élément | État |
|---|---|
| Calcul de disposition, pur et testé | DONE |
| `WindowManagerService`, mode STACK | DONE |
| Tailles 60/70/80/90 personnalisables, plein écran sans bordure | DONE |
| Centrage et commande Recentrer | DONE |
| Parcours circulaire des sessions | DONE |
| Choix de l'écran Windows | DONE |
| Contrôleur Win32 réel | DONE (à éprouver en phase 13) |

## Phase 10 - Raccourcis

| Élément | État |
|---|---|
| `HotkeyService` | TODO |
| Éditeur graphique + détection de conflits | TODO |

## Phase 11 - Paramètres et interface

| Élément | État |
|---|---|
| `SettingsService` + JSON robuste | TODO |
| Interface complète, premier lancement | TODO |

## Phase 12 - Logs, diagnostic, nettoyage

| Élément | État |
|---|---|
| Journalisation avec rotation | TODO |
| Page Diagnostic | TODO |
| Nettoyage des processus créés | TODO |

## Phase 13 - Tests avec téléphone réel

| Élément | État |
|---|---|
| Session de test manuelle | BLOCKED (nécessite un téléphone branché) |

## Phase 14 à 16 - Installateur, mises à jour, CI

| Élément | État |
|---|---|
| Empaquetage Velopack | TODO |
| Vérification des mises à jour + notes de version | TODO |
| GitHub Actions build/test/release | TODO |
| Dépôt GitHub public | TODO |
