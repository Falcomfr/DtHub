# DT Hub - consignes agent

Dépôt .NET 10 sous WSL, binaire Windows. Les règles rtk globales
(`~/.claude/RTK.md`) s'appliquent, avec les exceptions ci-dessous.

## .NET : toujours par un journal, jamais en direct

Le filtre `rtk dotnet` est cassé sur ce dépôt (vérifié en rtk 0.47.0 le
19/09/2026 et à nouveau en 0.50.0 le 26/09/2026, locale hors de cause) : il annonce `fail` sur une génération qui
réussit, compte « 1 projects » là où il y en a 14, et `rtk dotnet test` répond
`binlog-only mode, counts unavailable`, donc un test en échec resterait
invisible. Ne pas s'y fier.

La forme fiable passe par `rtk proxy` (qui court-circuite le filtre) et un
journal, puis un `grep` sur le résumé. Moins de tokens que le filtre rtk, et
le verdict est juste.

```bash
# build
rtk proxy dotnet build > /tmp/build.log 2>&1
grep -iE "error|erreur|warning|avertissement|réussi|succeeded|échec|failed" /tmp/build.log | tail -6

# test
rtk proxy dotnet test > /tmp/test.log 2>&1
grep -iE "erreur|échec|réussi|failed|passed|total" /tmp/test.log | tail -8

# publish (aucun filtre rtk pour cette sous-commande)
rtk proxy dotnet publish src/DtHub.App -c Release > /tmp/publish.log 2>&1
tail -5 /tmp/publish.log
```

Rendu attendu : 3 lignes pour un build, 2 pour les tests, contre plusieurs
centaines d'octets de sortie brute.

**Ne jamais conclure sur le seul code de sortie.** `rtk dotnet build` a déjà
renvoyé 0 en affichant `fail`, et la commande brute renvoie 0 sur succès comme
sur certains échecs partiels. Le verdict se lit dans le journal.

## Ne pas écrire `dotnet.exe`

Un `dotnet` est en place dans `~/.local/bin` et relaie vers
`/mnt/c/Program Files/dotnet/dotnet.exe`. Écrire `dotnet`, jamais `dotnet.exe` :
le moteur de réécriture rtk ne reconnaît aucune forme suffixée `.exe`.

## PowerShell et autres binaires Windows

`powershell.exe`, `cmd.exe`, `tasklist.exe`, `taskkill.exe` et `adb.exe` ne sont
couverts par aucun filtre : leur sortie part brute dans le contexte. Toujours
rediriger vers un journal puis filtrer.

```bash
powershell.exe -NoProfile -Command "..." > /tmp/ps.log 2>&1
grep -iE "error|exception|warning" /tmp/ps.log | head -20
```

## Lecture de fichier et recherche

Comme ailleurs : `cat` ou `head` (réécrits en `rtk read`), jamais `sed -n`.
`grep` et `ls` sont réécrits aussi. Les boucles `for ... done` échappent à la
réécriture : préférer une suite de commandes séparées par `;`.
