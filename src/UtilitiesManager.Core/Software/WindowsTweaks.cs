using System.Diagnostics;
using System.Security;
using UtilitiesManager.Profiles;

namespace UtilitiesManager.Software;

/// <summary>
/// Réglages Windows du profil : état lu dans le registre, application, rétablissement. Une valeur qui a déjà la
/// bonne valeur n'est pas réécrite. Une valeur HKLM refusée faute de droits est écrite par <c>reg.exe</c> lancé en
/// administrateur (<paramref name="runElevated"/>, une seule invite UAC par réglage).
/// </summary>
public sealed class WindowsTweaks(IRegistryStore registry, Func<string, Task<int>>? runElevated = null)
{
    private const int UacRefused = 1223;

    private static readonly Lazy<List<TweakEntry>> Catalog = new(TweakCatalog.Load);

    /// <summary>
    /// Valeurs à poser : celles du catalogue pour un réglage du catalogue (elles suivent Windows, qui en protège
    /// parfois une, comme <c>TaskbarDa</c>), sinon celles du profil.
    /// </summary>
    private static List<RegistrySetting> ValuesOf(TweakEntry tweak) =>
        (Catalog.Value.FirstOrDefault(c => c.Id == tweak.Id) ?? tweak).Values;

    /// <summary>Vrai si toutes les valeurs du réglage sont posées.</summary>
    public bool IsApplied(TweakEntry tweak) => ValuesOf(tweak).All(s => Has(s, s.Value));

    private bool Has(RegistrySetting setting, string value)
    {
        RegistryPath.TryParse(setting.Key, out var root, out var path);
        var current = registry.GetValue(root, path, setting.Name);
        return setting.Type == "dword"
            ? current is int number && int.TryParse(value, out var expected) && number == expected
            : current is string text && text == value;
    }

    private bool Exists(RegistrySetting setting)
    {
        RegistryPath.TryParse(setting.Key, out var root, out var path);
        return registry.GetValue(root, path, setting.Name) is not null;
    }

    public async Task ApplyAsync(TweakEntry tweak)
    {
        var elevated = new List<string>();
        foreach (var setting in ValuesOf(tweak).Where(s => !Has(s, s.Value)))
            Change(setting.Key, elevated, () => Write(setting, setting.Value), RegAdd(setting, setting.Value));
        await RunElevatedAsync(elevated).ConfigureAwait(false);
        Log.Info($"Réglage Windows appliqué : {tweak.Name}");
    }

    public async Task RestoreAsync(TweakEntry tweak)
    {
        var elevated = new List<string>();
        foreach (var setting in ValuesOf(tweak))
        {
            if (setting.RestoreDeleteKey is { } key)
            {
                RegistryPath.TryParse(key, out var keyRoot, out var keyPath);
                Change(key, elevated, () => registry.DeleteKey(keyRoot, keyPath), $"reg delete \"{key}\" /f /reg:64");
            }
            else if (setting.Restore is { } value)
            {
                if (!Has(setting, value))
                    Change(setting.Key, elevated, () => Write(setting, value), RegAdd(setting, value));
            }
            else if (Exists(setting))
            {
                RegistryPath.TryParse(setting.Key, out var root, out var path);
                Change(setting.Key, elevated, () => registry.DeleteValue(root, path, setting.Name),
                       $"reg delete \"{setting.Key}\" {ValueName(setting)} /f /reg:64");
            }
        }
        await RunElevatedAsync(elevated).ConfigureAwait(false);
        Log.Info($"Réglage Windows rétabli : {tweak.Name}");
    }

    /// <summary>Écrit tout de suite ; une clé HKLM refusée faute de droits est gardée pour la commande administrateur.</summary>
    private static void Change(string key, List<string> elevated, Action write, string command)
    {
        RegistryPath.TryParse(key, out var root, out _);
        try
        {
            write();
        }
        catch (Exception exc) when (root != RegistryRoot.CurrentUser && exc is UnauthorizedAccessException or SecurityException)
        {
            elevated.Add(command);
        }
    }

    private void Write(RegistrySetting setting, string value)
    {
        RegistryPath.TryParse(setting.Key, out var root, out var path);
        if (setting.Type == "dword")
            registry.SetDword(root, path, setting.Name, int.Parse(value));
        else
            registry.SetString(root, path, setting.Name, value);
    }

    private static string ValueName(RegistrySetting setting) => setting.Name.Length == 0 ? "/ve" : $"/v \"{setting.Name}\"";

    private static string RegAdd(RegistrySetting setting, string value) =>
        $"reg add \"{setting.Key}\" {ValueName(setting)} /t {(setting.Type == "dword" ? "REG_DWORD" : "REG_SZ")} /d \"{value}\" /f /reg:64";

    private async Task RunElevatedAsync(List<string> commands)
    {
        if (commands.Count == 0)
            return;
        if (runElevated is null)
            throw new UnauthorizedAccessException(L.T("Administrator rights are needed."));
        var code = await runElevated(string.Join(" && ", commands)).ConfigureAwait(false);
        if (code == UacRefused)
            throw new UnauthorizedAccessException(L.T("Administrator rights refused."));
        if (code != 0)
            throw new UnauthorizedAccessException(L.T("The command run as administrator failed (code {0}).", code));
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
