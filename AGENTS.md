# AGENTS.md

Guide destiné aux agents IA et aux contributeurs qui reprennent ce dépôt.
Lis ce fichier en entier avant de modifier quoi que ce soit.

## Ce qu'est DT Hub

Une application Windows de bureau qui pilote graphiquement le mirroring
d'appareils Android via ADB et scrcpy. Ce n'est pas un émulateur : les
applications tournent réellement sur les téléphones. L'outil se contente de
créer les sessions, d'organiser les fenêtres et de transmettre les entrées.

Le moteur est générique. Aucune logique, aucun nom et aucune ressource liés à
une application Android précise ne doivent apparaître dans le code.

## Environnement de développement

Le dépôt vit sur le disque Windows, à `C:\Dev\DTHub`. WPF ne se compile et ne
s'exécute que sous Windows. Si tu travailles depuis WSL, le chemin est
`/mnt/c/Dev/DTHub` et un lien pratique existe à `~/dev/DT Hub`.

Depuis WSL, appelle le SDK Windows par interopérabilité : `dotnet.exe`, jamais
`dotnet`. Place-toi dans un répertoire sous `/mnt/c` avant l'appel, sinon le
répertoire courant ne se traduit pas côté Windows.

Prérequis : .NET SDK 10, Git, GitHub CLI. `winget` sert à les installer.

## Commandes

```bash
dotnet.exe build DtHub.slnx                 # compilation complète
dotnet.exe test  DtHub.slnx                 # tests unitaires
dotnet.exe run --project src/DtHub.App      # lancer l'application
dotnet.exe format DtHub.slnx                # mise en forme
python3 build/make-icon.py                  # régénérer assets/app.ico
```

Le dépôt doit rester compilable et les tests verts à chaque commit.
Zéro avertissement est la cible : les analyseurs .NET sont actifs.

## Architecture

```
src/DtHub.Core            net10.0        modèles, contrats, logique pure, parseurs
src/DtHub.Infrastructure  net10.0        processus, ADB, scrcpy, Win32, persistance
src/DtHub.App             net10.0-windows  WPF, MVVM, vues et vues-modèles
tests/DtHub.Tests         net10.0        xUnit
```

Règles de dépendance, non négociables :

- `Core` ne référence rien. Aucun `System.Windows`, aucun P/Invoke, aucun
  accès disque ni réseau direct. Les parseurs y vivent parce qu'ils sont purs
  et testables sans téléphone.
- `Infrastructure` référence `Core` et implémente ses interfaces.
- `App` référence les deux et ne contient que de l'interface. Pas de logique
  métier dans le code-behind.
- `Tests` ne doit jamais avoir besoin d'un téléphone réel ni du réseau.

MVVM avec CommunityToolkit.Mvvm. Le code-behind reste limité à ce que XAML ne
sait pas exprimer.

## ADB

ADB n'est jamais supposé présent sur la machine de l'utilisateur. DT Hub
utilise sa propre copie, invoquée par chemin absolu, obtenue depuis la source
officielle Google et vérifiée. Ne jamais appeler `adb` via le `PATH`.

Toute exécution passe par `IProcessRunner`, ce qui permet de simuler ADB dans
les tests. Aucune console ne doit apparaître à l'écran : les processus sont
lancés sans fenêtre.

Chaque appel prend un délai maximal et un `CancellationToken`. Les erreurs sont
traduites en messages compréhensibles ; le détail technique part dans les logs.

## scrcpy

Une session scrcpy par `LaunchTarget`, c'est-à-dire par triplet
appareil + utilisateur Android + application.

Le lancement d'une application sur un utilisateur Android secondaire (clone,
profil professionnel, second espace) se fait sans forker scrcpy. Voir
`docs/DECISIONS.md` et `third_party/scrcpy/MODIFICATIONS.md`.

DT Hub ne modifie jamais l'application Android ciblée.

## Ce qui est interdit

- Toute automatisation de jeu : bot, macro, répétition d'actions,
  reconnaissance d'écran pour jouer, synchronisation d'entrées entre sessions,
  contournement d'une limitation applicative. Une entrée utilisateur
  correspond à une action manuelle et à une seule.
- PowerShell, AutoHotkey, Node.js ou Python à l'exécution de l'application.
  Ces outils sont tolérés dans les scripts de build uniquement.
- Toucher à Windows Defender, créer des exclusions antivirus, demander
  l'élévation au démarrage normal.
- Télécharger ou exécuter un binaire depuis une source non officielle. Les
  URL de dépendances sont centralisées et documentées.
- Coder en dur un numéro de série, une adresse IP, un identifiant
  d'utilisateur Android. En particulier, l'identifiant de clone ne vaut pas
  toujours 999.
- Écrire un code d'appairage ou un secret dans les logs.
- Utiliser un logo, une marque ou une ressource appartenant à un tiers comme
  élément d'identité du programme.

## Conventions

- Identifiants en anglais, commentaires et documentation en français,
  README en anglais.
- Pas de tiret cadratin dans les textes produits.
- Fichiers en UTF-8, fins de ligne CRLF via `.gitattributes`.
- `nullable` activé partout, pas de `catch (Exception)` muet.
- Tout appel pouvant durer est asynchrone et accepte un `CancellationToken`.
  L'interface ne doit jamais se figer.
- Nommage des tests : phrase descriptive en français avec underscores.
- Commits en français, à l'impératif, un sujet cohérent par commit.

## Renommer le produit

Trois endroits, dans cet ordre :

1. `Directory.Build.props` : `Product`, `ProductSlug`, `RepositoryUrl`.
2. `src/DtHub.Core/ProductInfo.cs` : `Name`, `Slug`, `RepositoryUrl`.
3. `README.md`, `installer/` et les workflows GitHub.

Les espaces de noms `DtHub.*` et les noms de projets restent inchangés : ce
sont des identifiants techniques, pas le nom commercial.

## Données utilisateur

Tout est sous `%LOCALAPPDATA%\<ProductSlug>\` :
`settings.json`, `devices.json`, `profiles.json`, `cache/`, `logs/`.

Un JSON illisible ne doit jamais faire planter l'application : le fichier
fautif est sauvegardé à côté et une configuration valide est recréée.

## Publier une version

1. Mettre à jour `CHANGELOG.md` et `VersionPrefix` dans `Directory.Build.props`.
2. Vérifier que le build et les tests passent.
3. Commiter, poser un tag `vX.Y.Z`, pousser le tag.
4. GitHub Actions compile, teste, publie en self-contained win-x64, empaquette
   avec Velopack et crée la release avec les notes du changelog.

Aucun secret ne doit être écrit dans le dépôt.

## Méthode de travail

Implémenter, compiler, tester, corriger, commiter, puis passer à la suite.
Tenir `docs/IMPLEMENTATION_STATUS.md` à jour. Consigner toute décision
d'architecture notable dans `docs/DECISIONS.md`. Ne pas déclarer terminée une
fonctionnalité qui repose encore sur un TODO critique.
