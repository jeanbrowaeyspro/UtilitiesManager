# Nouveautés

## [0.0.4] - 2026-11-16

### Ajouté
- Réglages Windows : bouton "Relever sur ce PC", qui ajoute au profil les réglages connus déjà modifiés sur ce PC (menu contextuel classique, extensions visibles, thème sombre...).

### Corrigé
- Compte GitHub connecté sans dépôt UtilitiesProfile : message "Dépôt introuvable" et bouton "Créer le dépôt" dans Paramètres, au lieu de "Profil injoignable, se connecter".
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
