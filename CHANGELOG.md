# Nouveautés

## [Non publié]

### Ajouté
- Onglet Réglages Windows : tous les réglages connus sont affichés, même ceux qui ne sont pas encore dans le profil (colonne "Dans le profil"). "Appliquer" sur l'un d'eux l'ajoute au profil.
- Réglage "Compte local, sans demande de compte Microsoft" : interdit de passer la session en compte Microsoft et supprime les écrans et rappels qui en demandent un.

## [1.0.2] - 2026-11-18

### Ajouté
- Onglet Paramètres : mode par défaut du profil (automatique ou avec confirmation), suivi par chaque logiciel laissé sur "Défaut du profil".
- Onglet Réglages Windows : tableau des réglages, boutons "Ajouter…", "Modifier…" (ou double-clic) et "Retirer du profil". La fenêtre d'un réglage donne son nom, le redémarrage de l'Explorateur et les valeurs du registre posées et rétablies.
- Fenêtre d'un logiciel : retouches de fichiers .ini posées après l'installation (fichier, section, clé, valeur).
- Onglet Paramètres : désinstallations refusées sur ce PC, avec "Proposer de nouveau".

## [1.0.1] - 2026-11-18

### Modifié
- Changer seulement le mode d'un logiciel (automatique ou avec confirmation) ne relance plus de vérification : la liste prend le nouveau mode tout de suite et, pour un logiciel passé en automatique, la mise à jour déjà connue est installée.

## [1.0.0] - 2026-11-17

Première version stable, qui reprend toutes les versions d'essai 0.0.1 à 0.0.11.

### Ajouté
- Un seul outil pour installer et tenir à jour les logiciels du profil (winget, Microsoft Store, Chocolatey, GitHub, zip, téléchargement, commande), avec une seule demande de droits administrateur par série d'installations, et "Arrêter" ou "Pause" pendant les installations.
- Profil rangé dans le dépôt GitHub privé `UtilitiesProfile` de chaque utilisateur (connexion par code, renouvelée d'elle-même), avec une copie locale sur le PC.
- Options par logiciel : mode automatique ou avec confirmation, âge minimum d'une version, version bloquée, processus à fermer, retour à la version précédente.
- "Relever sur ce PC" : logiciels installés et réglages Windows déjà modifiés, absents du profil, à y ajouter.
- Réglages Windows du profil : appliquer ou rétablir.
- Vérification à l'ouverture de session, chaque jour ou à la demande, icône de notification avec le nombre d'actions en attente et bilan de la vérification.
- Ligne de commande (`check`, `scan`, `install`, `update`, `revert`, `settings`, `profile`) avec résultat en JSON et code de sortie.
- Mise à jour de UtilitiesManager proposée, jamais silencieuse, avec contrôle SHA-256.

## [0.0.11] - 2026-11-17

### Corrigé
- Réglage de la barre des tâches : les widgets sont masqués par la stratégie Windows (l'ancien réglage est protégé par Windows et refusé). Ce réglage demande les droits administrateur, et un réglage déjà en place n'est plus réécrit.
- Chocolatey : le dossier d'extraction de l'installeur est créé avant l'installation (FileZilla échouait sur un chemin introuvable).

## [0.0.10] - 2026-11-17

### Corrigé
- Un logiciel absent qui ne sert que de dépendance (ex. Outil de déploiement Office pour Microsoft 365) n'est plus compté en attente : il est indiqué "Installé au besoin avec ..." et s'installe avec le logiciel qui en dépend. Il reste installable seul en le cochant.
- Quand winget ne trouve plus l'installeur chez l'éditeur (paquet winget pas encore mis à jour), le message le dit clairement au lieu d'afficher un code d'erreur seul.

## [0.0.9] - 2026-11-17

### Ajouté
- "Vérifier maintenant" du menu de l'icône : notification "Vérification lancée…", puis un bilan à la fin (logiciels à mettre à jour, installer ou désinstaller, mises à jour automatiques faites, nouvelle version de UtilitiesManager, erreurs, ou "Tout est à jour"). Un clic sur le bilan ouvre la fenêtre de mise à jour de UtilitiesManager s'il y en a une, sinon la fenêtre principale.
- Ce même bouton cherche aussi une nouvelle version de UtilitiesManager.
- "Vérifier maintenant" est grisé pendant une vérification ou une installation, et l'infobulle de l'icône indique "Vérification des logiciels…" pendant la vérification.

### Corrigé
- Avec plusieurs écrans à des mises à l'échelle différentes, la fenêtre se rouvre sur son écran, même agrandie (elle pouvait se rouvrir sur l'écran principal).

## [0.0.8] - 2026-11-17

### Ajouté
- Bouton "Relever sur ce PC…" dans l'onglet Logiciels : liste les logiciels installés sur ce PC et absents du profil (winget, programmes inscrits dans Windows, dossier des logiciels portables, "C:\Logiciels" par défaut). Les logiciels cochés connus de winget sont ajoutés directement au profil. Les autres s'ouvrent un par un dans la fenêtre du logiciel, préremplie avec la source trouvée (winget, Chocolatey, GitHub ou page de téléchargement à compléter), à choisir dans "Source proposée".
- "Ne plus proposer" : logiciels du relevé écartés sur ce PC, que "Proposer de nouveau" remet dans la liste.
- Paramètres : dossier des logiciels portables parcouru par le relevé.
- Ligne de commande : `scan` liste les logiciels de ce PC absents du profil.

## [0.0.7] - 2026-11-17

### Corrigé
- Connexion GitHub renouvelée d'elle-même quand elle expire (au bout de 8 heures) : le profil n'est plus déclaré injoignable avec "Jeton GitHub refusé ou expiré (401)".

## [0.0.6] - 2026-11-17

### Ajouté
- Tableau des logiciels suivi pendant les installations : "En attente", "En cours", "En pause", "À jour" (avec la nouvelle version), "Échec" ou "Non traité" après un arrêt.
- Une mise à jour ne remet plus d'icône sur le Bureau : les icônes ajoutées par l'installeur sont retirées juste après la mise à jour de chaque logiciel. Une première installation garde son icône.

## [0.0.5] - 2026-11-16

### Ajouté
- Une seule demande de droits administrateur pour toute une série d'installations (au lieu d'une par installeur). Refusée, chaque installeur les demande comme avant. Les applications du Microsoft Store s'installent toujours sans droits administrateur.
- Bouton "Arrêter" pendant les installations (aussi dans le menu de l'icône de notification) : le logiciel en cours va jusqu'au bout, les suivants ne sont pas traités et leurs téléchargements sont supprimés.
- Bouton "Pause" / "Reprendre" (aussi dans le menu de l'icône) : les installations attendent après le logiciel en cours.
- Ligne de commande : Ctrl+C arrête les installations après le logiciel en cours.

## [0.0.4] - 2026-11-16

### Ajouté
- Réglages Windows : bouton "Relever sur ce PC", qui ajoute au profil les réglages connus déjà modifiés sur ce PC (menu contextuel classique, extensions visibles, thème sombre...).

### Modifié
- Installations plus rapides : les fichiers des logiciels suivants se téléchargent pendant l'installation en cours (hors winget, qui télécharge lui-même). Les installations restent une par une.
- Tableau des logiciels : texte centré en hauteur, aligné sur les cases à cocher.

### Corrigé
- Compte GitHub connecté sans dépôt UtilitiesProfile : message "Dépôt introuvable" et bouton "Créer le dépôt" dans Paramètres, au lieu de "Profil injoignable, se connecter".
- Le bandeau indique le dépôt GitHub dès qu'une modification du profil y est enregistrée (il pouvait encore afficher "copie locale").
- Connexion à GitHub : la page ne s'ouvre plus d'elle-même, seulement par "Copier et ouvrir GitHub" (elle s'ouvrait deux fois).

## [0.0.3] - 2026-11-16

### Corrigé
- Erreur "le thread appelant ne peut pas accéder à cet objet" à la fin de la connexion à GitHub.
- La fenêtre se rouvre à sa dernière position, sur le même écran, avec sa taille et son état agrandi.

## [0.0.2] - 2026-11-16

### Ajouté
- Ligne de commande : vérifier, installer, mettre à jour, revenir à la version précédente, appliquer ou rétablir un réglage Windows, importer ou relire le profil, avec résultat en JSON et code de sortie.

### Modifié
- Sans compte GitHub, le profil est gardé et modifiable sur le PC, vide au premier lancement.
- Le profil est rangé dans le dépôt privé `UtilitiesProfile` du compte GitHub de chaque utilisateur. À la première connexion, l'outil guide la création du dépôt et l'accès à ce seul dépôt, puis propose d'y envoyer la copie locale du profil.

### Corrigé
- Icône nette dans la fenêtre "À propos" (elle était agrandie depuis sa plus petite image).

## [0.0.1] - 2026-11-16

### Ajouté
- Première version d'essai : un seul outil pour installer et tenir à jour les logiciels du profil (winget, Microsoft Store, Chocolatey, GitHub, zip, téléchargement, commande).
- Profil des logiciels dans un dépôt GitHub privé, avec une copie locale en secours : ajouter ou retirer un logiciel ne demande aucune nouvelle version de l'outil.
- Options par logiciel : mode automatique ou avec confirmation, âge minimum d'une version, version bloquée, processus à fermer, retour à la version précédente.
- Réglages Windows du profil : appliquer ou rétablir.
- Vérification à l'ouverture de session puis chaque jour, icône de notification avec le nombre d'actions en attente.
- Mise à jour de UtilitiesManager proposée, jamais silencieuse, avec contrôle SHA-256.
