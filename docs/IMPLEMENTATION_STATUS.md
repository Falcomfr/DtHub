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
| `AndroidUserService` (`pm list users`) | TODO |
| Support de tout `userId` entier | TODO |

## Phase 6 - Applications

| Élément | État |
|---|---|
| `AppDiscoveryService` | TODO |
| Icônes, labels, repli sur le package | TODO |
| Cache + favoris + recherche | TODO |

## Phase 7 - Profils de lancement

| Élément | État |
|---|---|
| `LaunchProfile` / `LaunchTarget` | TODO |
| `ProfileService` + persistance | TODO |

## Phase 8 - scrcpy

| Élément | État |
|---|---|
| `ScrcpyService` | TODO |
| Sessions indépendantes | TODO |
| Lancement sur profil Android secondaire | TODO |
| Patch documenté du serveur scrcpy | TODO |

## Phase 9 - Fenêtres

| Élément | État |
|---|---|
| `WindowManagerService` | TODO |
| Mode STACK, centrage, tailles 60/70/80/90/plein écran | TODO |

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
