namespace UtilitiesManager;

/// <summary>
/// Journal texte horodaté, utile surtout pour le démarrage invisible. Au-delà de
/// <see cref="MaxBytes"/>, le fichier est renommé en <c>.old.log</c> (une seule
/// génération conservée).
/// </summary>
public static class Log
{
    public const long MaxBytes = 256 * 1024;
    private static readonly Lock Gate = new();

    /// <summary>Fichier du journal ; vide = journal désactivé.</summary>
    public static string Path { get; private set; } = "";

    public static void Configure(string path) => Path = path;

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message) => Write("ERREUR", message);

    private static void Write(string level, string message)
    {
        if (Path.Length == 0)
            return;
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                var file = new FileInfo(Path);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(Path, System.IO.Path.ChangeExtension(Path, ".old.log"), overwrite: true);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
            }
            catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
            {
                // Le journal ne doit jamais empêcher l'application de fonctionner.
            }
        }
    }
}
