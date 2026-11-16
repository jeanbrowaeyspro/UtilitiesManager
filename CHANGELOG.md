# Nouveautés

## [0.0.2] - 2026-11-16

### Ajouté
- Ligne de commande : vérifier, installer, mettre à jour, revenir à la version précédente, appliquer ou rétablir un réglage Windows, importer ou relire le profil, avec résultat en JSON et code de sortie.

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
