# Changelog

Toutes les modifications notables de ce projet sont consignées ici.

Le format suit [Keep a Changelog](https://keepachangelog.com/fr/1.1.0/)
et le projet respecte le [versionnage sémantique](https://semver.org/lang/fr/).

## [Non publié]

### Ajouté

- Suivi de quêtes adossé à papycha.fr : fenêtre toujours au-dessus ouverte par
  `Ctrl+Q`, recherche par rubrique et par succès sur un catalogue de 782 quêtes,
  sélecteur d'étape et navigation dans la chaîne du succès.
- Contrôle de la version d'Android avant le lancement : un appareil sous
  Android 10 ou plus ancien est écarté aussitôt, en disant sa version, au lieu
  d'échouer au bout de trente secondes sur un message deviné.
- Fiches d'aide pour vivo et iQOO, et pour Amazon Fire, avec l'avertissement que
  Fire OS n'a pas le Play Store.
- Manifeste applicatif déclarant la conscience de la mise à l'échelle écran par
  écran et le socle Windows 10 1809.

### Modifié

- Le repli de définition descend les paliers au lieu de s'arrêter à 1080, ce qui
  couvre enfin les encodeurs plafonnés à 1280x720, et conserve le rapport
  d'image de l'écran au lieu d'imposer du 16:9.
- Les refus de scrcpy sont rangés en catégories : seuls ceux qu'une définition
  plus modeste peut réparer sont retentés. Un téléphone débranché ne coûte plus
  une seconde attente.
- La détection du jeu retient les copies installées sous un nom de paquet
  dérivé, que la comparaison stricte rendait invisibles.
- Le repli de la liste des profils Android est signalé dans le bandeau au lieu
  de passer pour un appareil qui n'a qu'un profil.
- Les chemins de menu de l'aide valent aussi pour une tablette, et les trois
  fenêtres d'aide disent qu'ils supposent un appareil réglé en français.
- Le repli d'Android sans surcouche nomme les constructeurs qu'il couvre :
  ASUS, TCL, ZTE, HMD, Fairphone, Transsion.

### Corrigé

- La densité d'affichage était bornée à 800 au calcul puis rabotée à 640 juste
  avant scrcpy : tout ce qui se trouvait entre les deux disparaissait en
  silence, et le zoom le plus proche saturait avant la hauteur annoncée.
- Un codec vidéo inconnu de scrcpy est écarté au lieu de lui être transmis.
- Depuis la recherche de quêtes, la flèche du bas s'arrêtait sur un intertitre
  de rubrique et Entrée ne faisait rien.

- Squelette de la solution .NET 10 : `DtHub.Core`, `DtHub.Infrastructure`,
  `DtHub.App` (WPF) et `DtHub.Tests` (xUnit).
- Identité produit centralisée pour permettre un renommage simple.
- Icône applicative générée par script reproductible.
