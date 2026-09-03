# Signaler une faille

DT Hub télécharge deux outils tiers, se met à jour depuis GitHub, et pilote un
téléphone par ADB. Si vous trouvez quelque chose qui touche à la sécurité,
merci de le dire en privé plutôt que dans une issue publique.

**Comment.** Ouvrez un avis de sécurité privé sur le dépôt :
[Security > Report a vulnerability](https://github.com/Falcomfr/DtHub/security/advisories/new).
GitHub le garde entre vous et moi jusqu'à ce qu'un correctif existe.

**Si ce formulaire ne s'ouvre pas**, c'est que le signalement privé n'est pas
activé sur le dépôt. N'écrivez rien de technique en public pour autant : ouvrez
une issue ordinaire disant seulement que vous avez trouvé quelque chose qui
touche à la sécurité, sans le décrire, et j'ouvrirai le canal privé. Une
politique de sécurité qui renvoie vers une porte fermée est pire que pas de
politique du tout, d'où cette porte de secours.

**Ce qui aide.** La version affichée par l'application, ce qui se passe, et ce
qu'il faut faire pour le reproduire. Le rapport de diagnostic de l'application
convient : il est déjà biffé de ce qui identifie.

**Ce à quoi vous pouvez vous attendre.** Une réponse sous une semaine. DT Hub
est écrit par une seule personne sur son temps libre : il n'y a ni astreinte,
ni prime, ni promesse de délai de correction.

## Ce qui compte comme une faille ici

- Un moyen de faire exécuter du code par DT Hub, ou de lui faire poser un
  fichier qu'il n'a pas vérifié.
- Un contournement des contrôles de mise en place : empreinte, taille, HTTPS.
- Une donnée personnelle qui survit à la biffure du rapport de diagnostic, ou
  qui atteint les journaux alors qu'elle ne le devrait pas. Les codes
  d'appairage en font partie.
- Une sortie du cloisonnement de la fenêtre des guides, qui ne doit jamais
  quitter papycha.fr ni ouvrir autre chose que du HTTPS.

## Ce qui n'en est pas une

- L'avertissement SmartScreen au premier lancement. Le binaire n'est pas signé
  et `docs/CONFIANCE.md` explique pourquoi et ce qu'il faudrait pour y remédier.
- Le fait que l'application interroge GitHub à chaque démarrage. C'est dit dans
  le README, et rien n'est installé sans qu'on le demande.
- Un antivirus qui s'inquiète d'un exécutable .NET auto-extractible non signé.
  C'est attendu, c'est documenté, et la réponse n'est jamais d'ajouter une
  exclusion.
