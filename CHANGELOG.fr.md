# Journal des modifications

Traduction française de [CHANGELOG.md](CHANGELOG.md), version par version, à
partir de celle qui suit la 0.5.2. Chaque section est jointe à la livraison
sous le nom `notes.fr.md` : c'est elle que l'application affiche en français,
et elle qu'annonce le salon Discord.

Les rubriques suivent celles de l'anglais : `### Ajouté`, `### Modifié`,
`### Corrigé`.

## [0.6.1] - 2026-09-28

### Corrigé

- Retirer une application ajoutée par le bouton + ne montrait rien pendant
  quelques secondes, le temps de balayer à nouveau le téléphone, et la corbeille
  semblait morte. La ligne indique maintenant qu'elle travaille jusqu'à ce que
  l'application ait quitté la liste.

## [0.6.0] - 2026-09-28

### Ajouté

- Un réglage éteint l'écran du téléphone pendant que ses comptes jouent sur le
  PC. Le jeu continue de tourner, le téléphone chauffe moins et sa batterie
  tient plus longtemps, ce qui compte quand le port USB du PC recharge moins
  vite que le jeu ne consomme. L'écran se rallume quand le dernier compte se
  ferme.
- Le bouton + d'un téléphone ouvre maintenant une fenêtre avec deux choix
  distincts. Afficher une application déjà présente sur le téléphone, trouvée en
  cherchant son nom, sans rien installer ni copier : une copie du jeu faite par
  une application de clonage, ou n'importe quelle autre application, s'ouvre
  alors dans sa propre fenêtre comme le jeu. Ou cloner le jeu sur un nouveau
  compte, comme le faisait le bouton jusqu'ici.
- Une application ajoutée ainsi peut être retirée de la liste avec la corbeille
  de sa ligne. Elle reste sur le téléphone. Les lignes du jeu lui-même sont
  toujours affichées.

### Modifié

- Dans le cadre à onglets, chaque onglet prend la couleur de son compte : on
  distingue les comptes d'un coup d'œil, et plus seulement par une fine barre.
