# Nouveautés

## [0.0.9] - 2026-11-17

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
