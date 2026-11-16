using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Windows.Forms;

namespace UtilitiesManager.Launcher;

/// <summary>
/// Installation du runtime .NET Desktop 10 (x64) : accord de l'utilisateur,
/// téléchargement de l'installeur officiel dans %TEMP%, exécution (l'installeur
/// demande lui-même l'élévation), puis nouvelle vérification.
/// </summary>
public static class RuntimeSetup
{
    private const string RuntimeName = ".NET Desktop Runtime 10 (x64)";

    /// <summary>Vrai si le runtime est installé à la sortie.</summary>
    public static bool Run()
    {
        var answer = MessageBox.Show(
            L.T("The {0} is required. Download and install it now?", RuntimeName),
            Program.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            Program.Error(L.T("UtilitiesManager cannot start without the {0}.", RuntimeName));
            return false;
        }

        var installer = Path.Combine(Path.GetTempPath(), "windowsdesktop-runtime-win-x64.exe");
        var error = DownloadForm.Download(RuntimeCheck.InstallerUrl, installer);
        if (error is not null)
        {
            Program.Error(L.T("Download of the {0} failed:\n\n{1}", RuntimeName, error));
            return false;
        }

        int code;
        try
        {
            // Installation sans question, avec sa barre de progression ; ShellExecute
            // laisse l'installeur demander l'élévation (UAC).
            using var process = Process.Start(new ProcessStartInfo(installer, "/install /passive /norestart")
            {
                UseShellExecute = true,
            })!;
            process.WaitForExit();
            code = process.ExitCode;
        }
        catch (Win32Exception exc)
        {
            // 1223 : élévation refusée.
            Program.Error(L.T("Installation of the {0} cancelled:\n\n{1}", RuntimeName, exc.Message));
            return false;
        }
        finally
        {
            TryDelete(installer);
        }

        if (RuntimeCheck.IsInstalled(RuntimeCheck.CandidateRoots()))
            return true;
        Program.Error(L.T("The {0} was not installed (code {1}).", RuntimeName, code));
        return false;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            // Fichier temporaire : sans conséquence.
        }
    }

    /// <summary>Petite fenêtre de progression pendant le téléchargement.</summary>
    private sealed class DownloadForm : Form
    {
        private readonly WebClient _client = new();
        private readonly ProgressBar _progress = new() { Dock = DockStyle.Bottom, Height = 18 };
        private string? _error = L.T("Download interrupted.");

        /// <summary>Télécharge le fichier ; renvoie null si tout s'est bien passé, sinon l'erreur.</summary>
        public static string? Download(string url, string destination)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using var form = new DownloadForm();
            form.Shown += (_, _) => form._client.DownloadFileAsync(new Uri(url), destination);
            form.ShowDialog();
            return form._error;
        }

        private DownloadForm()
        {
            Text = Program.Title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(420, 70);
            Padding = new Padding(12);
            Controls.Add(new Label { Text = L.T("Downloading the {0}…", RuntimeName), Dock = DockStyle.Top, Height = 28 });
            Controls.Add(_progress);
            _client.DownloadProgressChanged += (_, e) => _progress.Value = e.ProgressPercentage;
            _client.DownloadFileCompleted += (_, e) =>
            {
                _error = e.Cancelled ? L.T("Download cancelled.") : e.Error?.Message;
                Close();
            };
            // Fermeture par l'utilisateur : téléchargement annulé.
            FormClosing += (_, _) => _client.CancelAsync();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _client.Dispose();
            base.Dispose(disposing);
        }
    }
}
