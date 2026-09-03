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

### 1. Signer, ce qui règle l'essentiel

Trois routes, à vérifier chez le fournisseur car les conditions changent :

**Azure Trusted Signing**, chez Microsoft. La moins chère, de l'ordre de dix
euros par mois, sans jeton matériel puisque la clef reste chez eux. Elle demande
une identité vérifiée ; pour un particulier, un historique vérifiable de
plusieurs années est exigé. C'est la route à regarder en premier.

**Un certificat OV** chez une autorité de certification. De l'ordre de deux à
quatre cents euros par an, livré sur un jeton matériel ou dans un coffre en
nuage depuis que les clefs logicielles ne sont plus admises. La réputation
SmartScreen se construit ensuite, sur quelques semaines et quelques centaines de
téléchargements.

**Un certificat EV**, plus cher, qui accorde la réputation SmartScreen dès la
première signature. C'est le seul moyen de n'avoir aucun avertissement le
premier jour.

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
