# Faire reconnaître l'application

DT Hub est un exécutable de soixante mégaoctets, distribué hors des magasins,
qui se met à jour tout seul et télécharge deux outils tiers. Chacun de ces
traits est, pris isolément, ce que fait aussi un logiciel malveillant. Ce
document dit ce qui est déjà fait, ce qui manque, et dans quel ordre le régler.

**Ce qui ne sera jamais fait :** ajouter une exclusion à Windows Defender, ou
demander à quelqu'un de le faire. Une application qui a besoin qu'on désarme
l'antivirus n'est pas une application de confiance, c'est une application qui
demande un privilège.

## Où on en est, mesuré

| Point | État |
|---|---|
| Analyse Defender du binaire publié | Aucune menace |
| Nom du produit dans le fichier | « DT Hub », conforme à ce que dit la fenêtre |
| Description dans le fichier | Une phrase, pas un nom de fichier |
| Éditeur | Falcomfr |
| Signature Authenticode | **Absente** |
| Réputation SmartScreen | **Aucune**, le fichier n'ayant jamais été distribué |

Le contrôle d'identité est rejoué à chaque livraison par la chaîne, qui refuse
de livrer un binaire dont le nom, la description ou l'éditeur manquent. C'est
ainsi qu'on a vu que le fichier annonçait « DT Touch » des semaines après le
renommage.

## Ce qui déclenchera des alertes, et pourquoi

**L'absence de signature, en premier.** Windows SmartScreen met en garde sur tout
exécutable téléchargé qu'il ne connaît pas, signé ou non ; mais un binaire signé
capitalise sa réputation sur le certificat, donc sur toutes ses versions à la
fois, quand un binaire non signé repart de zéro à chaque livraison. Sans
signature, l'avertissement « Windows a protégé votre PC » reviendra
indéfiniment.

**La mise à jour automatique.** Télécharger un exécutable et remplacer le sien
est le comportement d'un installeur de charge utile. Ce qui l'en distingue ici :
l'adresse est celle d'un dépôt public, l'empreinte est vérifiée avant que le
fichier ne serve, et rien ne s'exécute avant le lancement suivant. Cela ne
convaincra pas une heuristique, cela convaincra un analyste.

Il faut dire ce que cette empreinte garantit et ce qu'elle ne garantit pas :
elle est lue dans le fichier `.sha256` de la **même** livraison. Elle protège
donc du transport, d'un téléchargement tronqué ou altéré en chemin. Elle ne
protège pas de la publication d'une fausse livraison, qui porterait sa propre
empreinte. Ce qui protège de cela est le code public, et la signature quand
elle existera.

**Le téléchargement d'ADB et de scrcpy.** Dix-neuf mégaoctets d'outils tiers,
pris aux sources officielles, empreintes vérifiées, adresses centralisées dans
`build/dependencies.json`. Un analyste vérifiera ce fichier. L'utilisateur, lui,
voit au premier lancement une fenêtre qui nomme chaque composant, son éditeur et
l'adresse d'où il vient, puis montre le téléchargement et la vérification.

**Le fichier unique auto-extractible.** Un exécutable qui se décompresse
ressemble à un binaire empaqueté. C'est la forme normale d'une application .NET
autonome, et les moteurs la connaissent, mais elle compte dans un score.

## Ce qu'il reste à faire, par ordre d'effet

### 1. Signer, ce qui aide, mais pas comme on le croit

**Aucun certificat ne fait taire SmartScreen le premier jour.** C'est la
première chose à savoir, et elle contredit ce que vendent encore la plupart des
revendeurs. Vérifié en septembre 2026.

Le certificat EV a longtemps accordé la réputation d'emblée. Microsoft a changé
ce comportement en mars 2024 : EV reste le certificat de plus haut niveau de
vérification, exigé pour les pilotes, mais il n'achète plus le silence de
SmartScreen. Payer le supplément pour cette seule raison n'a plus de sens.

Ce que la signature achète vraiment, et qui vaut le prix :

- le nom de l'éditeur à la place d'« Éditeur inconnu » dans l'avertissement ;
- une réputation qui **s'accumule d'une version à l'autre** au lieu de repartir
  de zéro à chaque livraison, ce qui est le sort d'un binaire non signé.

Trois routes, à revérifier chez le fournisseur car les conditions bougent vite.

**SignPath Foundation, gratuit, et c'est la route à regarder en premier.** Elle
signe gratuitement les projets libres, avec des certificats Sectigo de niveau OV.
Son mécanisme est plus exigeant que l'achat d'un certificat, et c'est ce qui en
fait la valeur : elle vérifie que le binaire a bien été bâti depuis le dépôt
public, et engage son nom là-dessus.

Deux conditions bloquent aujourd'hui, et toutes deux figurent déjà dans ce qui
reste à faire : le dépôt doit être **public**, et le projet doit **déjà avoir une
livraison** dans la forme à signer. La licence MIT convient. L'examen du dossier
prend de quelques jours à quelques semaines.

Un point à vérifier sur leurs conditions avant de s'engager : l'éditeur affiché
est celui de la fondation, qui se porte garante du projet, et non « Falcomfr ».
C'est un choix, pas un détail.

Les deux autres routes, payantes :

**Azure Artifact Signing**, chez Microsoft, renommé en 2026 et anciennement
Trusted Signing. Environ dix dollars par mois pour cinq mille signatures, sans
jeton matériel puisque la clef reste chez eux. Ouvert aux entreprises et aux
**indépendants** vérifiés de l'Union européenne, du Royaume-Uni, des États-Unis
et du Canada : c'est la première condition à vérifier, un particulier sans
statut n'y entre pas. Comptez quelques jours ouvrés de vérification d'identité.

Une réserve sérieuse, relevée en 2026 : Microsoft fait tourner ses autorités
intermédiaires, et plusieurs utilisateurs voient l'avertissement SmartScreen
réapparaître à chaque livraison parce que la nouvelle autorité n'a pas encore de
réputation. La réputation d'un binaire signé chez eux n'est donc pas acquise une
fois pour toutes.

**Un certificat OV ou EV** chez une autorité de certification. De deux cent
cinquante à six cents dollars par an, sur jeton matériel ou dans un coffre en
nuage, les clefs logicielles n'étant plus admises. Depuis février 2026, la
validité est plafonnée à un an, ce qui rend le renouvellement annuel obligatoire.
La réputation se construit ensuite, sur quelques semaines et quelques centaines
de téléchargements.

Une fois le certificat obtenu, rien à changer dans le dépôt : poser un secret
`SIGNING_COMMAND` sur le dépôt GitHub, contenant la ligne de signature complète
du fournisseur. La chaîne l'exécute sur le binaire publié, vérifie que la
signature a pris, et refuse de livrer si elle n'a pas pris. Sans ce secret,
l'étape est sautée et la livraison sort non signée.

### 2. Vérifier avant de livrer

Avant la première diffusion, envoyer le binaire sur VirusTotal et regarder ce
que les soixante moteurs en disent. **Attention : envoyer un fichier à
VirusTotal le publie**, il devient accessible aux abonnés du service. Ce n'est
pas un problème pour une application dont le code est public, c'en serait un
pour un binaire privé.

Un ou deux moteurs marginaux qui crient au loup sur un binaire .NET non signé
est banal. Une dizaine, ou un moteur majeur, mérite d'être compris avant de
diffuser.

### 3. Faire lever une fausse alerte

Si un moteur se trompe, chaque éditeur a un formulaire de soumission. Pour
Microsoft, c'est le portail « Submit a file for analysis » du centre de sécurité,
en choisissant « Faux positif ». Le délai est de quelques jours. Joindre le lien
du dépôt et celui de la livraison : un code source public est l'argument le plus
efficace.

### 4. Ce qui aide sans rien coûter

- Livrer depuis le dépôt, avec l'empreinte à côté du binaire, ce qui est déjà
  le cas. Quelqu'un doit pouvoir vérifier ce qu'il a téléchargé.
- Garder le code public : c'est ce qui distingue une fausse alerte d'un doute.
- Ne pas changer le nom du fichier d'une version à l'autre, la réputation s'y
  attachant.
- Ne jamais demander de droits administrateur, ce que l'application ne fait pas
  et ne doit pas se mettre à faire.

## Vérifier soi-même l'état d'un binaire

```powershell
# Identité et signature
$f = 'chemin\DtHub.exe'
(Get-Item $f).VersionInfo | Format-List ProductName, FileDescription, CompanyName, FileVersion
Get-AuthenticodeSignature $f | Format-List Status, SignerCertificate

# Analyse par Defender, sans rien modifier à ses réglages
& 'C:\Program Files\Windows Defender\MpCmdRun.exe' -Scan -ScanType 3 -File $f
```
