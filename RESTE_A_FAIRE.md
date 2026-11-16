# Reste à faire

## À lancer par Jean

- Essai de la version 0.0.1 sur un PC : installation de l'outil, import de `profile.json` (copie locale du dépôt
  UtilitiesProfile), vérification, installation d'un logiciel winget, d'un logiciel par commande, d'un outil
  portable (BareTail), retour à la version précédente, réglages Windows appliqués puis rétablis.

## À faire

- Créer l'application GitHub (GitHub App) de Jean, accès en lecture et écriture au seul dépôt UtilitiesProfile,
  et poser son Client ID dans `GitHubSignIn.ApplicationClientId`. Sans elle, pas de connexion GitHub : profil importé
  à la main, et les fichiers `profile:` (zip EdiTSAB, `office_config.xml`) comme la release privée de HAIC ne
  peuvent pas être téléchargés.
- Signataires de Npcap et d'iVMS-4200 à relever sur un installeur téléchargé, puis `verify: signature` et `signer`
  dans le profil (aujourd'hui sans contrôle de signature, installeurs ouverts pour Jean).
- Retouches de fichiers `.ini` (`ini` du profil, cas Greenshot) : non modifiables dans l'éditeur de l'outil,
  seulement dans le fichier du profil.

## À confirmer

- EdiTSAB : `EdiTSAB.emf` (modifié le 3 juillet) est peut-être une donnée de Jean. Le zip du profil l'écrase à
  chaque installation.
- VirtualBox Extension Pack : la clé `--accept-license` est celle de la licence actuelle d'Oracle, elle change si
  Oracle modifie la licence.
- Claude Code : aucune dernière version connue sans lancer `claude`, la mise à jour reste celle de Claude Code
  lui-même.
