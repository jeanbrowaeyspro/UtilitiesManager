using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace UtilitiesManager.Launcher;

/// <summary>Recherche du runtime .NET Desktop dont l'application a besoin.</summary>
/// <remarks>
/// Les dossiers examinés sont ceux où le lanceur natif de .NET le chercherait :
/// variables <c>DOTNET_ROOT</c>, emplacement inscrit dans le registre, puis
/// <c>Program Files\dotnet</c>.
/// </remarks>
public static class RuntimeCheck
{
    public const string Framework = "Microsoft.WindowsDesktop.App";
    /// <summary>Version majeure requise ; toute version 10.x convient.</summary>
    public const int Major = 10;
    /// <summary>Installeur officiel de la dernière version 10.x (adresse stable de Microsoft).</summary>
    public const string InstallerUrl = "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe";

    /// <summary>Dossiers d'installation de .NET possibles sur ce poste.</summary>
    public static IEnumerable<string> CandidateRoots()
    {
        foreach (var name in new[] { "DOTNET_ROOT_X64", "DOTNET_ROOT" })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value))
                yield return value!;
        }
        var registered = RegisteredLocation();
        if (!string.IsNullOrEmpty(registered))
            yield return registered!;
        var programFiles = Environment.GetEnvironmentVariable("ProgramW6432")
                           ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(programFiles, "dotnet");
    }

    private static string? RegisteredLocation()
    {
        try
        {
            // .NET inscrit son emplacement dans la vue 32 bits du registre.
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = hive.OpenSubKey(@"SOFTWARE\dotnet\Setup\InstalledVersions\x64");
            return key?.GetValue("InstallLocation") as string;
        }
        catch (Exception exc) when (exc is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    /// <summary>Vrai si l'un des dossiers contient une version <see cref="Major"/>.x du runtime.</summary>
    public static bool IsInstalled(IEnumerable<string> roots) => roots.Any(HasFramework);

    public static bool HasFramework(string root)
    {
        var folder = Path.Combine(root, "shared", Framework);
        if (!Directory.Exists(folder))
            return false;
        return Directory.GetDirectories(folder)
                        .Select(d => Path.GetFileName(d).Split('-')[0])   // "10.0.0-rc.1..." : préversion
                        .Any(name => Version.TryParse(name, out var v) && v.Major == Major);
    }
}
