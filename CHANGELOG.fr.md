# Journal des modifications

Traduction française de [CHANGELOG.md](CHANGELOG.md), version par version, à
partir de celle qui suit la 0.5.2. Chaque section est jointe à la livraison
sous le nom `notes.fr.md` : c'est elle que l'application affiche en français,
et elle qu'annonce le salon Discord.

Les rubriques suivent celles de l'anglais : `### Ajouté`, `### Modifié`,
`### Corrigé`.

Une entrée tient en une ligne courte qui dit ce que le joueur y gagne ; le
pourquoi reste dans les commits et dans `docs/DECISIONS.md`.

## [0.6.1] - 2026-09-28

### Corrigé

- Retirer une app ajoutée affiche un chargement au lieu de sembler ne rien
  faire.

## [0.6.0] - 2026-09-28

### Ajouté

- Option pour éteindre l'écran du téléphone pendant le jeu : il chauffe moins
  et la batterie tient plus.
- Le bouton + peut afficher n'importe quelle app déjà installée, comme un
  DOFUS Touch cloné, ou cloner le jeu sur un nouveau compte comme avant.
- Une app ajoutée ainsi se retire avec la corbeille de sa ligne.

### Modifié

- Les onglets prennent la couleur de leur compte.
