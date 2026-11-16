# UtilitiesManager

Outil Windows qui installe et tient à jour les logiciels d'un PC à partir d'un profil : la liste des logiciels,
leur source et leurs options. Il remplace les petits outils de mise à jour propres à un seul logiciel.

Livrable : un seul fichier, `UtilitiesManager.exe`, sans droits administrateur pour l'installer. Il demande le
runtime .NET Desktop 10 et propose de l'installer s'il manque.

## Installation

1. Télécharger `UtilitiesManager.exe` depuis la page des
   [versions](https://github.com/jeanbrowaeyspro/UtilitiesManager/releases).
2. Le lancer et accepter l'installation : il se copie dans `%LOCALAPPDATA%\Programs\UtilitiesManager`, se lance à
   l'ouverture de session et reste dans la zone de notification.
3. Charger le profil : onglet "Paramètres", connexion à GitHub ou import d'un fichier `profile.json`.

## Utilisation

- **Logiciels** : état de chaque logiciel du profil (installé, à jour, mise à jour disponible, en attente, absent).
  Cocher puis "Installer / mettre à jour". Par logiciel : mode (automatique ou à valider), âge minimum d'une
  version, version bloquée, retour à la version précédente.
- **Réglages Windows** : appliquer ou rétablir les réglages du profil (menu contextuel classique, extensions
  visibles, barre des tâches...).
- **Paramètres** : profil, connexion GitHub, lancement au démarrage, raccourcis, thème et langue.
- L'icône de notification indique le nombre d'actions en attente. La vérification a lieu à l'ouverture de
  session, puis chaque jour, ou à la demande ("Vérifier maintenant").

Un logiciel absent du poste est toujours proposé à l'installation, jamais installé en silence. Un logiciel retiré
du profil est proposé à la désinstallation.

## Ligne de commande

`UtilitiesManager.exe <commande> [--json]`, sans fenêtre, même si l'outil tourne déjà en arrière-plan.

| Commande | Effet |
|---|---|
| `check` | Vérifie les logiciels du profil et les réglages Windows |
| `install <id>` ou `install --all` | Installe un logiciel absent (ou tous) |
| `update <id>` ou `update --all` | Installe une mise à jour disponible (ou toutes) |
| `revert <id>` | Revient à la version précédente gardée sur le PC |
| `settings apply <id>` / `settings restore <id>` | Applique ou rétablit un réglage Windows |
| `profile import <fichier>` | Importe un fichier de profil (copie locale) |
| `profile sync` | Relit le profil sur GitHub |
| `help` | Aide |

Avec `--json`, le résultat est écrit en JSON sur la sortie standard et l'avancement sur la sortie d'erreur. Codes
de sortie : 0 fait, 1 échec, 2 ligne de commande incorrecte, 3 actions en attente, 4 autre opération en cours.
Chaque commande est écrite dans le journal. Les questions éventuelles reçoivent "non" et la fenêtre de contrôle de
compte d'utilisateur (UAC) reste affichée.

## Sources prises en charge

| Source | Exemple |
|---|---|
| winget | la plupart des logiciels |
| Microsoft Store (par winget) | WhatsApp, Spotify |
| Chocolatey (installeur extrait du paquet, sans Chocolatey) | FileZilla |
| Release GitHub | outils publiés sur GitHub |
| Zip ou exe portable | outils de `C:\Logiciels` |
| Téléchargement (page ou adresse fixe) | Npcap, VirtualBox Extension Pack |
| Commande | paquets Python, extensions VS Code, outils .NET |

Tout installeur qui ne vient pas de winget est contrôlé avant d'être lancé : signature Authenticode de l'éditeur
attendu, empreinte SHA-256 ou origine (dépôt GitHub). La fenêtre de contrôle de compte d'utilisateur (UAC) n'est
jamais masquée.

## Profil

Le profil est un fichier JSON. Ajouter, modifier ou retirer un logiciel se fait depuis l'outil ou dans le fichier,
sans nouvelle version de l'outil.

Sans compte GitHub, le profil est gardé sur le PC (vide au premier lancement, ou importé d'un fichier). Avec un
compte GitHub, il est sauvegardé dans le dépôt privé `UtilitiesProfile` de l'utilisateur, avec une copie locale qui
sert hors ligne.

À la première connexion (Paramètres > Se connecter), l'outil ouvre la page GitHub de création du dépôt, puis celle
qui autorise UtilitiesManager à accéder à ce seul dépôt. Si le dépôt est vide, il propose d'y envoyer la copie
locale du profil.

Logiciel publié dans un autre dépôt GitHub privé (source "Release GitHub") : sur GitHub, Settings > Applications >
UtilitiesManager > Configure, ajouter ce dépôt à la liste des dépôts autorisés. Aucune modification de l'outil
n'est nécessaire.

## Fichiers

| Emplacement | Contenu |
|---|---|
| `%LOCALAPPDATA%\Programs\UtilitiesManager` | l'outil installé |
| `%APPDATA%\UtilitiesManager` | réglages |
| `%LOCALAPPDATA%\UtilitiesManager` | copie du profil, état, journal, version précédente de chaque logiciel |

## Développement

`build.cmd` produit `dist\UtilitiesManager.exe` et son empreinte `.sha256`. Une étiquette `vX.Y.n` (version
d'essai) ou `vX.Y` (livraison) publie la version sur GitHub (`.github\workflows\release.yml`).
