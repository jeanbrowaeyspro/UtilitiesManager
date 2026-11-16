using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace UtilitiesManager.Launcher;

/// <summary>
/// Vérifie le runtime .NET Desktop 10 (propose de l'installer), extrait
/// UtilitiesManager.App.exe dans %LOCALAPPDATA%\UtilitiesManager si besoin (rien n'est
/// écrit à côté du lanceur), puis la démarre en lui
/// transmettant la ligne de commande et le chemin du lanceur.
/// </summary>
public static class Program
{
    public const string Title = "UtilitiesManager";

    /// <summary>Commande en ligne de commande : messages sur la sortie d'erreur, jamais de boîte de dialogue.</summary>
    private static bool _command;

    [STAThread]
    public static int Main()
    {
        L.Use(L.LanguageIn(AppLocations.SettingsFile(AppLocations.RoamingAppData)));
        var args = Environment.GetCommandLineArgs();
        _command = args.Length > 1 && Switches.IsCommand(args[1]);
        try
        {
            if (!RuntimeCheck.IsInstalled(RuntimeCheck.CandidateRoots()) && !RuntimeSetup.Run())
                return 1;
            var launcher = Assembly.GetExecutingAssembly().Location;
            var app = AppLocations.ExtractedApp(AppLocations.DataDir(AppLocations.LocalAppData));
            if (!PrepareApp(app))
                return 1;
            var start = new ProcessStartInfo(app, CommandLine.ForApp(launcher, Environment.CommandLine))
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory,
            };
            if (!_command)
            {
                Process.Start(start)?.Dispose();
                return 0;
            }
            return RunCommand(start);
        }
        catch (Exception exc)
        {
            Error(L.T("Cannot start:\n\n{0}", exc.Message));
            return 1;
        }
    }

    /// <summary>Extrait l'application incorporée si elle manque ou diffère de celle en place.</summary>
    private static bool PrepareApp(string app)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedApp.ResourceName);
        if (stream is null)
        {
            // Lanceur de développement, compilé sans l'application.
            if (File.Exists(app))
                return true;
            Error(L.T("Application not found:\n{0}", app));
            return false;
        }
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        try
        {
            EmbeddedApp.Extract(buffer.ToArray(), app);
            return true;
        }
        catch (IOException) when (File.Exists(app))
        {
            // Application en cours d'exécution : celle en place est démarrée, elle refusera
            // une deuxième instance.
            return true;
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            Error(L.T("Cannot write {0} to the folder:\n{1}\n\n{2}",
                      EmbeddedApp.ResourceName, Path.GetDirectoryName(app), exc.Message));
            return false;
        }
    }

    /// <summary>
    /// Commande : attend la fin de l'application et relaie ses sorties telles quelles (octets UTF-8) vers celles du
    /// lanceur ; rend son code de sortie.
    /// </summary>
    private static int RunCommand(ProcessStartInfo start)
    {
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.CreateNoWindow = true;
        using (var process = Process.Start(start))
        using (var output = Console.OpenStandardOutput())
        using (var error = Console.OpenStandardError())
        {
            var relayOutput = process.StandardOutput.BaseStream.CopyToAsync(output);
            var relayError = process.StandardError.BaseStream.CopyToAsync(error);
            process.WaitForExit();
            System.Threading.Tasks.Task.WaitAll(relayOutput, relayError);
            return process.ExitCode;
        }
    }

    public static void Error(string text)
    {
        if (_command)
        {
            using (var error = new StreamWriter(Console.OpenStandardError(), new System.Text.UTF8Encoding(false)))
                error.WriteLine(text);
            return;
        }
        MessageBox.Show(text, Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
