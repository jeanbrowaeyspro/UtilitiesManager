using System.Collections.Generic;

namespace UtilitiesManager;

/// <summary>
/// Traductions françaises des textes de l'interface, indexées par le texte anglais
/// (<see cref="L.T(string)"/>). Un test vérifie que chaque texte des sources y figure.
/// Fichier lié au lanceur et à l'application : interne à chacun.
/// </summary>
internal static class FrenchCatalog
{
    public static readonly Dictionary<string, string> Texts = new()
    {
        // Mots courants
        ["Yes"] = "Oui",
        ["No"] = "Non",
        ["OK"] = "OK",
        ["Cancel"] = "Annuler",
        ["Close"] = "Fermer",
        ["Install"] = "Installer",
        ["Later"] = "Plus tard",
        ["Apply"] = "Appliquer",
        ["Uninstall"] = "Désinstaller",
        ["none"] = "aucune",
        ["unknown"] = "inconnue",
        ["not installed"] = "non installé",
        ["{0}: {1}"] = "{0} : {1}",
        ["{0} (code {1})."] = "{0} (code {1}).",

        // Lanceur
        ["Cannot start:\n\n{0}"] = "Démarrage impossible :\n\n{0}",
        ["Application not found:\n{0}"] = "Application introuvable :\n{0}",
        ["Cannot write {0} to the folder:\n{1}\n\n{2}"] = "Impossible d'écrire {0} dans le dossier :\n{1}\n\n{2}",
        ["The {0} is required. Download and install it now?"] = "Le {0} est nécessaire. Le télécharger et l'installer maintenant ?",
        ["UtilitiesManager cannot start without the {0}."] = "UtilitiesManager ne peut pas démarrer sans le {0}.",
        ["Download of the {0} failed:\n\n{1}"] = "Téléchargement du {0} impossible :\n\n{1}",
        ["Installation of the {0} cancelled:\n\n{1}"] = "Installation du {0} annulée :\n\n{1}",
        ["The {0} was not installed (code {1})."] = "Le {0} n'a pas été installé (code {1}).",
        ["Download interrupted."] = "Téléchargement interrompu.",
        ["Download cancelled."] = "Téléchargement annulé.",
        ["Downloading the {0}…"] = "Téléchargement du {0}…",

        // Chocolatey et paquet
        ["Chocolatey does not answer: {0}"] = "Chocolatey ne répond pas : {0}",
        ["Chocolatey did not give the address of the package."] = "Chocolatey n'a pas donné l'adresse du paquet.",
        ["Chocolatey limits the number of requests, try again later."] = "Chocolatey limite le nombre de requêtes, réessayer plus tard.",
        ["Unexpected answer from Chocolatey (HTTP {0})."] = "Réponse inattendue de Chocolatey (HTTP {0}).",
        ["Unexpected package address: {0}"] = "Adresse de paquet inattendue : {0}",
        ["The package does not contain the 64-bit FileZilla installer."] = "Le paquet ne contient pas l'installeur 64 bits de FileZilla.",
        ["The package contains {0} instead of version {1}."] = "Le paquet contient {0} au lieu de la version {1}.",

        // Mise à jour de FileZilla
        ["FileZilla {0} is available (installed version: {1}). Install now?"] = "FileZilla {0} est disponible (version installée : {1}). L'installer maintenant ?",
        ["FileZilla is not installed. Install it now?"] = "FileZilla n'est pas installé. L'installer maintenant ?",
        ["FileZilla is open. Close it for the installation? It will be restarted afterwards."] = "FileZilla est ouvert. Le fermer pour l'installation ? Il sera relancé ensuite.",
        ["Close FileZilla"] = "Fermer FileZilla",
        ["Downloading and installing FileZilla {0}…\n\nAllow the changes to the device if Windows asks."] = "Téléchargement et installation de FileZilla {0}…\n\nAutoriser les modifications de l'appareil si Windows le demande.",
        ["The installation failed"] = "L'installation a échoué",
        ["The update failed"] = "La mise à jour a échoué",
        ["The FileZilla installer is not validly signed by {0} (signer: {1}). Nothing was installed."] = "L'installeur de FileZilla n'est pas signé valablement par {0} (signataire : {1}). Rien n'a été installé.",
        ["FileZilla {0} is installed."] = "FileZilla {0} est installé.",
        ["The installation of FileZilla {0} has finished, but the installed version read back is {1}."] = "L'installation de FileZilla {0} est terminée, mais la version installée relue est {1}.",
        ["Unexpected error:\n\n{0}"] = "Erreur inattendue :\n\n{0}",

        // Réglages
        ["UtilitiesManager settings"] = "Réglages de UtilitiesManager",
        ["Installed version"] = "Version installée",
        ["Latest version (Chocolatey)"] = "Dernière version (Chocolatey)",
        ["Check now"] = "Vérifier maintenant",
        ["Checking…"] = "Vérification…",
        ["FileZilla is not installed."] = "FileZilla n'est pas installé.",
        ["FileZilla {0} is available."] = "FileZilla {0} est disponible.",
        ["FileZilla is up to date."] = "FileZilla est à jour.",
        ["Options"] = "Options",
        ["Start with Windows"] = "Démarrer avec Windows",
        ["Desktop shortcut"] = "Raccourci sur le Bureau",
        ["Start menu shortcut"] = "Raccourci dans le menu Démarrer",
        ["Theme"] = "Thème",
        ["Windows theme"] = "Thème de Windows",
        ["Light"] = "Clair",
        ["Dark"] = "Sombre",
        ["Language"] = "Langue",
        ["Windows language"] = "Langue de Windows",
        ["Run UtilitiesManager.exe to be able to install it."] = "Lancer UtilitiesManager.exe pour pouvoir l'installer.",
        ["Installation finished."] = "Installation terminée.",
        ["Options applied."] = "Options appliquées.",
        ["Operation not possible:\n\n{0}"] = "Opération impossible :\n\n{0}",
        ["Uninstall UtilitiesManager?\n\nStarting with Windows and the shortcuts will be removed and the installation folder deleted. FileZilla stays installed."] = "Désinstaller UtilitiesManager ?\n\nLe démarrage avec Windows et les raccourcis seront retirés et le dossier d'installation supprimé. FileZilla reste installé.",
        ["UtilitiesManager is uninstalled."] = "UtilitiesManager est désinstallé.",
    };
}
