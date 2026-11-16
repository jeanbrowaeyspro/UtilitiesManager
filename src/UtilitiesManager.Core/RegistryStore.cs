using Microsoft.Win32;

namespace UtilitiesManager;

/// <summary>Branches du registre utilisées par l'application.</summary>
public enum RegistryRoot
{
    CurrentUser,
    /// <summary>HKLM, vue 64 bits.</summary>
    LocalMachine64,
    /// <summary>HKLM, vue 32 bits (WOW6432Node).</summary>
    LocalMachine32,
}

/// <summary>Accès au registre, remplacé par un faux dans les tests.</summary>
public interface IRegistryStore
{
    IReadOnlyList<string> SubKeyNames(RegistryRoot root, string path);

    /// <summary>Valeur brute (chaîne ou entier), null si la clé ou la valeur manque.</summary>
    object? GetValue(RegistryRoot root, string path, string name);

    void SetString(RegistryRoot root, string path, string name, string value);

    void SetDword(RegistryRoot root, string path, string name, int value);

    void DeleteValue(RegistryRoot root, string path, string name);

    /// <summary>Supprime la clé et toutes ses sous-clés, sans erreur si elle manque.</summary>
    void DeleteKey(RegistryRoot root, string path);
}

/// <summary>Clé écrite en toutes lettres dans le profil : <c>HKCU\...</c> ou <c>HKLM\...</c> (vue 64 bits).</summary>
public static class RegistryPath
{
    public static bool TryParse(string text, out RegistryRoot root, out string path)
    {
        root = RegistryRoot.CurrentUser;
        path = "";
        var separator = text.IndexOf('\\');
        if (separator <= 0 || separator == text.Length - 1)
            return false;
        switch (text[..separator].ToUpperInvariant())
        {
            case "HKCU" or "HKEY_CURRENT_USER":
                root = RegistryRoot.CurrentUser;
                break;
            case "HKLM" or "HKEY_LOCAL_MACHINE":
                root = RegistryRoot.LocalMachine64;
                break;
            default:
                return false;
        }
        path = text[(separator + 1)..];
        return true;
    }
}

public static class RegistryStoreExtensions
{
    public static string? GetString(this IRegistryStore registry, RegistryRoot root, string path, string name) =>
        registry.GetValue(root, path, name) as string;
}

/// <summary>Vrai registre Windows.</summary>
public sealed class WindowsRegistry : IRegistryStore
{
    private static RegistryKey Open(RegistryRoot root) => root switch
    {
        RegistryRoot.CurrentUser => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default),
        RegistryRoot.LocalMachine64 => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
        _ => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32),
    };

    public IReadOnlyList<string> SubKeyNames(RegistryRoot root, string path)
    {
        using var hive = Open(root);
        using var key = hive.OpenSubKey(path);
        return key?.GetSubKeyNames() ?? [];
    }

    public object? GetValue(RegistryRoot root, string path, string name)
    {
        using var hive = Open(root);
        using var key = hive.OpenSubKey(path);
        return key?.GetValue(name);
    }

    public void SetString(RegistryRoot root, string path, string name, string value)
    {
        using var hive = Open(root);
        using var key = hive.CreateSubKey(path);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void SetDword(RegistryRoot root, string path, string name, int value)
    {
        using var hive = Open(root);
        using var key = hive.CreateSubKey(path);
        key.SetValue(name, value, RegistryValueKind.DWord);
    }

    public void DeleteValue(RegistryRoot root, string path, string name)
    {
        using var hive = Open(root);
        using var key = hive.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }

    public void DeleteKey(RegistryRoot root, string path)
    {
        using var hive = Open(root);
        hive.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
    }
}
