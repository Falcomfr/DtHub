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
| Git initialisé + premier commit | IN PROGRESS | |
| AGENTS.md / README.md | IN PROGRESS | |

## Phase 2 - Exécution de processus et ADB

| Élément | État |
|---|---|
| `IProcessRunner` + implémentation | TODO |
| `AdbService` (start, devices, shell, timeout, annulation) | TODO |
| Parseurs `adb devices` / `devices -l` | TODO |
| Tests de parsing | TODO |

## Phase 3 - Découverte des appareils

| Élément | État |
|---|---|
| Modèles `AndroidDevice` | TODO |
| `DeviceDiscoveryService` | TODO |
| `getprop` (constructeur, modèle, version Android, SDK) | TODO |

## Phase 4 - Appairage Wi-Fi

| Élément | État |
|---|---|
| `DevicePairingService` (`adb pair`) | TODO |
| Découverte mDNS (`adb mdns services`) | TODO |
| Reconnexion automatique au démarrage | TODO |

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
