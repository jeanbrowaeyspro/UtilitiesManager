using System.Text;

namespace UtilitiesManager;

/// <summary>
/// Ecriture atomique d'un fichier : contenu ecrit dans &lt;fichier&gt;.tmp puis renomme, jamais de fichier a moitie ecrit
/// lu par un autre processus. Dossier parent cree au besoin.
/// </summary>
public static class AtomicFile
{
    private const string TemporarySuffix = ".tmp";

    private static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Ecrit un texte en UTF-8 sans BOM.</summary>
    public static void WriteAllText(string path, string text, bool overwrite = true) =>
        Write(path, overwrite, temporary => File.WriteAllText(temporary, text, Utf8));

    public static void WriteAllBytes(string path, byte[] bytes) =>
        Write(path, overwrite: true, temporary => File.WriteAllBytes(temporary, bytes));

    /// <summary>Copie un fichier. Sans ecrasement, une destination existante leve IOException.</summary>
    public static void Copy(string source, string path, bool overwrite = true) =>
        Write(path, overwrite, temporary => File.Copy(source, temporary, overwrite: true));

    private static void Write(string path, bool overwrite, Action<string> write)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + TemporarySuffix;
        write(temporary);
        try
        {
            File.Move(temporary, path, overwrite);
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }
}
