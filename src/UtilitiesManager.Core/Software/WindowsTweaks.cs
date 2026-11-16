using System.Diagnostics;
using UtilitiesManager.Profiles;

namespace UtilitiesManager.Software;

/// <summary>Réglages Windows du profil : état lu dans le registre, application, rétablissement.</summary>
public sealed class WindowsTweaks(IRegistryStore registry)
{
    /// <summary>Vrai si toutes les valeurs du réglage sont posées.</summary>
    public bool IsApplied(TweakEntry tweak) => tweak.Values.All(IsSet);

    private bool IsSet(RegistrySetting setting)
    {
        RegistryPath.TryParse(setting.Key, out var root, out var path);
        var current = registry.GetValue(root, path, setting.Name);
        return setting.Type == "dword"
            ? current is int number && int.TryParse(setting.Value, out var expected) && number == expected
            : current is string text && text == setting.Value;
    }

    public void Apply(TweakEntry tweak)
    {
        foreach (var setting in tweak.Values)
            Write(setting.Key, setting.Name, setting.Type, setting.Value);
        Log.Info($"Réglage Windows appliqué : {tweak.Name}");
    }

    public void Restore(TweakEntry tweak)
    {
        foreach (var setting in tweak.Values)
        {
            if (setting.RestoreDeleteKey is { } key)
            {
                RegistryPath.TryParse(key, out var keyRoot, out var keyPath);
                registry.DeleteKey(keyRoot, keyPath);
            }
            else if (setting.Restore is { } value)
            {
                Write(setting.Key, setting.Name, setting.Type, value);
            }
            else
            {
                RegistryPath.TryParse(setting.Key, out var root, out var path);
                registry.DeleteValue(root, path, setting.Name);
            }
        }
        Log.Info($"Réglage Windows rétabli : {tweak.Name}");
    }

    private void Write(string key, string name, string type, string value)
    {
        RegistryPath.TryParse(key, out var root, out var path);
        if (type == "dword")
            registry.SetDword(root, path, name, int.Parse(value));
        else
            registry.SetString(root, path, name, value);
    }

    /// <summary>Redémarre l'Explorateur pour qu'il relise ses réglages (menu contextuel, barre des tâches).</summary>
    public static void RestartExplorer()
    {
        foreach (var process in Process.GetProcessesByName("explorer"))
        {
            using (process)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch (Exception exc) when (exc is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    Log.Error($"Arrêt de l'Explorateur : {exc.Message}");
                }
            }
        }
        // Windows relance l'Explorateur de lui-même en général ; sinon il est relancé ici.
        Thread.Sleep(1500);
        if (Process.GetProcessesByName("explorer").Length == 0)
            Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true })?.Dispose();
    }
}
