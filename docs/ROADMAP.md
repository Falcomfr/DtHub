# Feuille de route

Le versionnage suit [SemVer](https://semver.org/lang/fr/). Tant que la version
majeure est `0`, l'interface et les formats de configuration peuvent changer.

## v0.1 - Base technique

- Solution .NET 10, architecture en couches, build et tests automatisés.
- Exécution de processus abstraite et testable.
- `AdbService` avec ADB embarqué et chemin absolu.
- Découverte des appareils par USB, lecture des propriétés via `getprop`.

## v0.2 - Multi-appareils et sélecteur d'applications

- Appairage Wi-Fi assisté (`adb pair`, découverte mDNS, reconnexion auto).
- Page Appareils : renommer, reconnecter, oublier, appareil principal.
- Utilisateurs et profils Android (`pm list users`), clones inclus.
- Découverte des applications par appareil et par utilisateur, avec cache,
  recherche, favoris et vues Favoris / Applications / Système.

## v0.3 - Sessions, fenêtres et raccourcis

- `ScrcpyService` et sessions indépendantes par cible de lancement.
- Lancement sur un utilisateur Android secondaire (patch scrcpy documenté).
- `WindowManagerService`, mode STACK, centrage, presets de taille.
- `HotkeyService` et éditeur graphique de raccourcis.

## v0.4 - Installation et mises à jour

- Empaquetage Velopack, installateur autonome sans prérequis.
- Vérification des mises à jour au démarrage, notes de version affichées.
- GitHub Actions : build, tests, publication sur tag.

## v1.0 - Stable

- Formats de configuration figés et migrations gérées.
- Diagnostic complet et gestion d'erreurs éprouvée.
- Documentation utilisateur finalisée, interface FR/EN.
- Préparation de la signature Authenticode.

## Envisagé après la v1.0

- Mode Grid en complément du mode STACK.
- Profils d'affichage par écran Windows.
- Traduction de l'interface.
