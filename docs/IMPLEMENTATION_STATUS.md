# État d'avancement

Légende : **DONE** terminé et vérifié, **IN PROGRESS** en cours,
**TODO** pas commencé, **BLOCKED** nécessite une action externe.

Dernière mise à jour : 2026-09-04

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
| Les douze raccourcis au clavier, trois contextes chacun | DONE |

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
| Thème sombre. Il n'existe pas de palette claire | DONE |
| Barre de titre sombre sur les fenêtres qui gardent celle de Windows | DONE |
| Échelle typographique nommée, six crans | DONE |
| Journalisation avec rotation, dossier accessible | DONE |
| Cadre à onglets : loger, réordonner, sortir, fermer | DONE |
| Fenêtre de préparation au premier lancement, sources nommées | DONE |
| Rapport d'incident biffé, à copier et à envoyer | DONE |
| Interface en anglais, français et espagnol, fiches de marques comprises | DONE |
| Suivi de quêtes adossé à papycha.fr, dans sa propre fenêtre | DONE |
| Clavier : Entrée et Échap sur les boîtes de dialogue | DONE |

## Distribution

| Élément | État |
|---|---|
| Publication en fichier unique, 60 Mo, sans installateur | DONE |
| Décision : pas d'installateur ni de Velopack | DONE |
| Signature Authenticode | TODO (non nécessaire pour un usage personnel) |
| Dépôt GitHub public | TODO |
| Première livraison étiquetée | TODO |
| GitHub Actions build et tests | DONE |
| Chaîne : forme, permissions restreintes, actions épinglées | DONE |
| Contrôles d'artefact : un seul fichier, plancher de poids | DONE |
| Mise à jour depuis le dépôt, empreinte vérifiée | DONE |
| Attribution de papycha.fr, hors du champ de la licence MIT | DONE |
| Historique purgé des captures de développement | DONE |

## Compatibilité

Audité le 2026-08-31, appareil par appareil et poste par poste.

| Élément | État |
|---|---|
| Aucun chemin, adresse IP ni numéro de série en dur | DONE |
| Lecture des propriétés par listes de clés alternatives | DONE |
| Définition de l'afficheur calculée sur le moniteur du PC, non sur l'appareil | DONE |
| Aucun identifiant de profil Android déduit ou supposé | DONE |
| Analyse numérique en culture invariante | DONE |
| Version d'Android contrôlée avant le lancement | DONE |
| Repli de définition descendant les paliers, jusqu'à 720 | DONE |
| Refus de scrcpy rangés en catégories avant de retenter | DONE |
| Copies du jeu à nom de paquet dérivé détectées | DONE |
| Liste de profils illisible signalée | DONE |
| Conscience de la mise à l'échelle écran par écran | DONE |
| Chemins de menu valables pour une tablette | DONE |
| Windows sur ARM | BLOCKED (scrcpy n'y est pas distribué) |
| Fiches de marques vérifiées ailleurs que sur Xiaomi | TODO |
| Lancement sur profil secondaire vérifié sur un second appareil | TODO |

## Reste à faire

| Élément | État |
|---|---|
| Essai complet du lancement des deux comptes | DONE |
| Options supplémentaires dans l'onglet Général | TODO (à définir) |
| Réglages de mirroring exposés dans l'interface | TODO |
