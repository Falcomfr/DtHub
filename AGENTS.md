# AGENTS.md

Guide destiné aux agents IA et aux contributeurs qui reprennent ce dépôt.
Lis-le en entier avant de modifier quoi que ce soit.

## Ce qu'est DT Hub

Une application Windows qui ouvre plusieurs comptes DOFUS Touch côte à côte,
chacun dans sa fenêtre, depuis de vrais téléphones Android. Ce n'est pas un
émulateur : le jeu tourne sur le téléphone, DT Hub crée l'affichage, lance le
jeu, organise les fenêtres et transmet les entrées.

Chaque compte vit sur un profil Android différent du téléphone : le profil
principal, et un profil cloné du type Second Space ou Applications dupliquées.
DT Hub trouve toutes les installations du jeu et en ouvre une par profil.

## Le fonctionnement, en deux cas

**Premier lancement.** Une fenêtre unique montre les téléphones connectés et,
sous chacun, les instances du jeu trouvées. L'utilisateur coche, valide, et la
question n'est plus jamais posée. S'il n'y a aucun téléphone, la fenêtre guide
l'association Wi-Fi et se met à jour toute seule ; il n'y a pas de bouton
« ajouter un appareil », brancher un câble suffit.

**Ensuite.** Les instances cochées s'ouvrent directement. Un configurateur
flottant se pose dans un coin libre, et `Ctrl+P` l'affiche ou le masque. Il a
trois onglets : Général, Appareils, Raccourcis. Rien d'autre n'existe.

## Environnement de développement

Le dépôt vit sur le disque Windows, à `C:\Dev\DTHub`. WPF ne se compile et ne
s'exécute que sous Windows. Depuis WSL, le chemin est `/mnt/c/Dev/DTHub` et un
lien pratique existe à `~/dev/DT Hub`.

Depuis WSL, appelle le SDK Windows par interopérabilité : `dotnet.exe`, jamais
`dotnet`. Place-toi dans un répertoire sous `/mnt/c` avant l'appel, sinon le
répertoire courant ne se traduit pas côté Windows.

Prérequis : .NET SDK 10, Git, GitHub CLI. `winget` sert à les installer.

## Commandes

```bash
dotnet.exe build DtHub.slnx                 # compilation complète
dotnet.exe test  DtHub.slnx                 # tests unitaires
dotnet.exe run --project src/DtHub.App      # lancer l'application
python3 build/make-icon.py                  # régénérer assets/app.ico
python3 build/extract-successes.py           # relever la carte des succès de papycha

# Vérifier que le site se lit encore comme l'application le suppose. Rend 1 en
# cas d'écart avec build/sonde-papycha/reference.json. À lancer avant de livrer.
dotnet.exe run --project build/sonde-papycha
dotnet.exe run --project build/sonde-papycha -- --benir   # rebénir le relevé

# Publier le fichier unique distribué à l'utilisateur.
dotnet.exe publish src/DtHub.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o build\publish
```

`build/lancer.cmd` rejoue cette publication puis ouvre l'application. C'est ce
que vise le raccourci du bureau, et non le binaire : viser le binaire ne
garantit rien, il date de la dernière publication et non de la dernière
modification. Mesuré : 1,1 s quand rien n'a changé, 10,6 s sinon. Aucune étape
n'est bloquante, un échec de publication lance quand même le binaire présent.
`build/create-shortcut.ps1` pose ou met à jour ce raccourci.

Le dépôt doit rester compilable et les tests verts à chaque commit. Zéro
avertissement est la cible : les analyseurs .NET sont actifs.

`build/capture-window.ps1` capture la fenêtre de l'application dans un PNG.
C'est l'outil qui permet de vérifier le rendu réel. Il se déclare conscient de
la mise à l'échelle : sans cela, il mesure la fenêtre trop petite et rogne la
capture, ce qui fait croire à un défaut de disposition inexistant.

## Architecture

```
src/DtHub.Core            net10.0          modèles, contrats, logique pure, parseurs
src/DtHub.Infrastructure  net10.0          processus, ADB, scrcpy, Win32, persistance
src/DtHub.App             net10.0-windows  WPF, MVVM, les deux fenêtres
tests/DtHub.Tests         net10.0          xUnit
```

Règles de dépendance, non négociables :

- `Core` ne référence rien. Aucun `System.Windows`, aucun P/Invoke, aucun
  accès disque ni réseau direct. Les parseurs y vivent parce qu'ils sont purs
  et testables sans téléphone.
- `Infrastructure` référence `Core` et implémente ses interfaces.
- `App` référence les deux et ne contient que de l'interface. Le code-behind
  se limite à ce que XAML ne sait pas exprimer : capture de touches,
  déplacement d'une fenêtre sans bordure, politique de fermeture.
- `Tests` ne doit jamais avoir besoin d'un téléphone réel ni du réseau.

## ADB

ADB n'est jamais supposé présent sur la machine de l'utilisateur. DT Hub
utilise sa propre copie, invoquée par chemin absolu, obtenue depuis la source
officielle Google et vérifiée. Ne jamais appeler `adb` via le `PATH`.

Toute exécution passe par `IProcessRunner`, ce qui permet de simuler ADB dans
les tests. Aucune console ne doit apparaître à l'écran.

Chaque appel prend un délai maximal et un `CancellationToken`. Les erreurs
sont traduites en messages compréhensibles ; le détail technique part dans les
journaux.

## scrcpy

Une session scrcpy par instance, chacune sur son propre afficheur virtuel.
DT Hub lit l'identifiant d'afficheur que scrcpy journalise à la création, puis
lance le jeu par ADB avec `--user`. scrcpy n'est pas modifié : voir
`third_party/scrcpy/MODIFICATIONS.md`.

`--kill-adb-on-close` est délibérément absent : il couperait le serveur ADB
pour toute la machine. Un test le vérifie.

## Ce qui est interdit

- Toute automatisation de jeu : robot, macro, répétition d'actions,
  reconnaissance d'écran pour jouer, synchronisation d'entrées entre comptes,
  contournement d'une limitation du jeu. Une entrée utilisateur correspond à
  une action, sur un compte, et à une seule.
- Utiliser une marque, un logo ou une ressource d'Ankama. DT Hub cite le nom
  du jeu pour dire ce qu'il fait, rien de plus.
- PowerShell, AutoHotkey, Node.js ou Python à l'exécution de l'application.
  Ces outils sont tolérés dans les scripts de développement uniquement.
- Toucher à Windows Defender, créer des exclusions antivirus, demander
  l'élévation.
- Un crochet clavier de bas niveau. Les raccourcis passent par
  `RegisterHotKey`, activé seulement quand une fenêtre de DT Hub est active.
- Télécharger ou exécuter un binaire depuis une source non officielle. Les URL
  sont centralisées dans `build/dependencies.json`.
- Coder en dur un numéro de série, une adresse IP, un identifiant de profil
  Android. Le profil cloné ne vaut pas toujours 999.
- Écrire un code d'appairage dans les journaux.

## Conventions

- Identifiants en anglais, commentaires et documentation en français,
  README en anglais.
- Pas de tiret cadratin dans les textes produits.
- Fichiers en UTF-8, fins de ligne LF dans le dépôt.
- `nullable` activé partout, pas de `catch (Exception)` muet.
- Tout appel pouvant durer est asynchrone et accepte un `CancellationToken`.
- Nommage des tests : phrase descriptive en français avec underscores.
- Commits en français, à l'impératif, un sujet cohérent par commit.

## Pièges déjà rencontrés

Ils ont tous coûté du temps une fois. Ne pas les réintroduire.

- **`InvariantGlobalization` casse WPF.** La liaison de données appelle
  `XmlLanguage.GetSpecificCulture`, qui échoue sans données de culture.
- **Un pinceau dans un dictionnaire de ressources est gelé.** Un
  `DynamicResource` sur sa couleur ne se résout jamais et l'interface
  s'affiche en noir. Les palettes portent leurs couleurs littéralement.
- **Un `ScrollViewer` mesure son contenu sur une largeur infinie** tant que
  `HorizontalScrollBarVisibility` n'est pas à `Disabled`.
- **Les convertisseurs doivent vivre au niveau application.** Une vue chargée
  par modèle de données n'a pas de parent quand son XAML est analysé, et ne
  verrait pas les ressources de la fenêtre.
- **`dotnet test` ne reconstruit pas le projet d'interface.** Le projet de
  tests ne référence que `DtHub.Core` et `DtHub.Infrastructure`. Lancer
  l'exécutable après un `dotnet test` fait tourner un binaire périmé, et on
  vérifie alors autre chose que ce qu'on vient d'écrire. Compiler
  explicitement `src/DtHub.App/DtHub.App.csproj` avant de lancer.
- **Un `Storyboard` déclenché depuis un gabarit ne voit pas son étendue de
  noms.** Un `Storyboard.TargetName` désignant un élément du `ControlTemplate`
  lève une exception à chaque affichage, pendant le `Loaded`, ce qui
  interrompt le rendu. Viser l'élément qui porte le déclencheur, sans nom.
- **Le `ComboBox` par défaut ignore le thème sombre.** Son gabarit est
  remplacé dans `Themes/Controls.xaml`.

## Renommer le produit

Deux endroits : `Directory.Build.props` et `src/DtHub.Core/ProductInfo.cs`.
Les espaces de noms `DtHub.*` restent des identifiants techniques et ne
suivent pas le nom commercial.

## Données utilisateur

Tout est sous `%LOCALAPPDATA%\<ProductSlug>\` : `settings.json`,
`devices.json`, `cache/`, `logs/`, `tools/`.

Un JSON illisible ne doit jamais faire planter l'application : le fichier
fautif est archivé à côté et une configuration valide est recréée.

## Méthode de travail

Implémenter, compiler, tester, corriger, commiter, puis passer à la suite.
Tenir `docs/IMPLEMENTATION_STATUS.md` à jour et consigner toute décision
structurante dans `docs/DECISIONS.md`.

Compiler ne prouve rien sur le rendu ni sur le comportement. Lancer
l'application et capturer sa fenêtre fait partie de la vérification, pas des
finitions.

### Lancer l'application depuis WSL

`Start-Process` doit recevoir un répertoire de travail Windows :

```
powershell.exe -NoProfile -Command "Start-Process -FilePath 'C:\Dev\DTHub\build\publish\DtHub.exe' -WorkingDirectory 'C:\Dev\DTHub\build\publish'"
```

Lancé depuis un chemin WSL, le processus hérite d'un répertoire courant UNC
`\\wsl.localhost\...`. Il démarre, reste vivant et répond, mais se fige avant
la première ligne de journal : la construction de l'hôte sonde ce chemin. Le
symptôme trompe, car il ressemble à un plantage de l'application.
