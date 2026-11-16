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

    string? GetString(RegistryRoot root, string path, string name);

    void SetString(RegistryRoot root, string path, string name, string value);

    void DeleteValue(RegistryRoot root, string path, string name);
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

    public string? GetString(RegistryRoot root, string path, string name)
    {
        using var hive = Open(root);
        using var key = hive.OpenSubKey(path);
        return key?.GetValue(name) as string;
    }

    public void SetString(RegistryRoot root, string path, string name, string value)
    {
        using var hive = Open(root);
        using var key = hive.CreateSubKey(path);
        key.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(RegistryRoot root, string path, string name)
    {
        using var hive = Open(root);
        using var key = hive.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
    }
}
