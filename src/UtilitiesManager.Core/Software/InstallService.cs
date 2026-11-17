using System.Diagnostics;
using System.IO.Compression;
using UtilitiesManager.GitHub;
using UtilitiesManager.Profiles;

namespace UtilitiesManager.Software;

/// <summary>Étape d'une installation, affichée dans la barre de progression et le journal de la fenêtre.</summary>
public sealed record InstallStep(string Program, string Text, double? Fraction = null);

public enum InstallResult
{
    Done,
    Cancelled,
    Failed,
}

/// <summary>
/// Fichier d'installation téléchargé et contrôlé (<paramref name="File"/> nul : commande sans fichier), rangé dans
/// le dossier temporaire <paramref name="Temp"/>.
/// </summary>
public sealed record Download(string Temp, string? File, string? Version)
{
    /// <summary>Supprime le dossier temporaire (sans effet s'il n'existe plus).</summary>
    public void Discard() => InstallService.TryDelete(Temp);
}

public sealed record InstallOutcome(InstallResult Result, string? Message = null, string? Version = null)
{
    public bool Succeeded => Result == InstallResult.Done;
}

/// <summary>
/// Installation, mise à jour, retour à la version précédente et désinstallation des logiciels du profil. Tout
/// fichier hors winget est contrôlé (<see cref="Integrity"/>) avant d'être lancé ; l'UAC n'est jamais masquée.
/// </summary>
public sealed class InstallService(Winget winget, Detector detector, LatestResolver resolver, ShellCommands shell, Integrity integrity,
                                   IProcessRunner runner, IAppProcesses processes, VersionCache cache, ProfileStore profiles,
                                   Func<string, GitHubRepositoryClient> github, MachineState state)
{
    /// <summary>Ordre d'installation : chaque dépendance avant le logiciel qui en dépend.</summary>
    public static IReadOnlyList<ProgramEntry> Order(IEnumerable<ProgramEntry> programs)
    {
        var list = programs.ToList();
        var ordered = new List<ProgramEntry>();
        void Visit(ProgramEntry program, int depth)
        {
            if (ordered.Contains(program) || depth > list.Count)
                return;
            if (program.DependsOn is { } dep && list.FirstOrDefault(p => p.Id == dep) is { } first)
                Visit(first, depth + 1);
            ordered.Add(program);
        }
        foreach (var program in list)
            Visit(program, 0);
        return ordered;
    }

    /// <summary>
    /// Vrai si le fichier d'installation peut être téléchargé pendant l'installation des logiciels précédents :
    /// winget télécharge lui-même, et la version d'une dépendance peut entrer dans l'adresse ou le contrôle.
    /// </summary>
    public static bool CanDownloadAhead(ProgramEntry program) => !program.IsWinget && program.DependsOn is null;

    /// <summary>Télécharge et contrôle à l'avance le fichier d'installation (<see cref="CanDownloadAhead"/>).</summary>
    public async Task<Download> DownloadAheadAsync(ProgramEntry program, CancellationToken cancel = default)
    {
        var temp = Directory.CreateTempSubdirectory("UtilitiesManager-").FullName;
        try
        {
            var before = await DetectAsync(program, cancel).ConfigureAwait(false);
            return await FetchAsync(program, before, null, temp, null, cancel).ConfigureAwait(false);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    /// <summary>
    /// Installe ou met à jour <paramref name="program"/> (sa dépendance d'abord si elle manque), avec le fichier
    /// téléchargé à l'avance <paramref name="ahead"/> s'il est donné (supprimé ensuite par l'appelant).
    /// </summary>
    public async Task<InstallOutcome> InstallAsync(ProgramEntry program, Profile profile, IProgress<InstallStep>? progress = null,
                                                   Task<Download>? ahead = null, CancellationToken cancel = default)
    {
        var temp = Directory.CreateTempSubdirectory("UtilitiesManager-").FullName;
        IReadOnlyList<string> closed = [];
        try
        {
            string? depVersion = null;
            if (program.DependsOn is { } depId && profile.Find(depId) is { } dependency)
            {
                var depInstalled = await DetectAsync(dependency, cancel).ConfigureAwait(false);
                if (!depInstalled.Present)
                {
                    var outcome = await InstallAsync(dependency, profile, progress, cancel: cancel).ConfigureAwait(false);
                    if (!outcome.Succeeded)
                        return outcome;
                    depInstalled = await DetectAsync(dependency, cancel).ConfigureAwait(false);
                }
                depVersion = depInstalled.Version;
            }

            var before = await DetectAsync(program, cancel).ConfigureAwait(false);
            if (before.Version is { } oldVersion && cache.CurrentOf(program.Id) is null)
                cache.Push(program.Id, oldVersion, null);

            if (program.Close is { Count: > 0 } names)
            {
                Report(progress, program, L.T("Closing {0}…", string.Join(", ", names)));
                closed = processes.Close(names);
            }

            var (result, file, version) = await RunSourceAsync(program, before, depVersion, temp, ahead, progress, cancel).ConfigureAwait(false);
            if (!result.Succeeded)
                return result;

            foreach (var ini in program.Ini ?? [])
                IniFile.Apply(Environment.ExpandEnvironmentVariables(ini.File), ini.Section, ini.Key, ini.Value);

            shell.Reset();
            var after = await DetectAsync(program, cancel).ConfigureAwait(false);
            var installed = after.Version ?? version;
            if (installed is not null && after.Version is null)
                state.InstalledVersions[program.Id] = installed;
            if (installed is not null)
                cache.Push(program.Id, installed, file);
            state.Remember(program);
            Log.Info($"{program.Name} installé : {before.Version ?? "absent"} -> {installed ?? "?"}");
            return new InstallOutcome(InstallResult.Done, null, installed);
        }
        catch (OperationCanceledException)
        {
            return new InstallOutcome(InstallResult.Cancelled, L.T("Cancelled."));
        }
        catch (Exception exc)
        {
            Log.Error($"{program.Id} : installation : {exc}");
            return new InstallOutcome(InstallResult.Failed, exc.Message);
        }
        finally
        {
            foreach (var exe in closed)
                processes.Start(exe);
            TryDelete(temp);
        }
    }

    private async Task<InstalledInfo> DetectAsync(ProgramEntry program, CancellationToken cancel)
    {
        IReadOnlyList<WingetPackage> packages = program.IsWinget ? await winget.ListAsync(cancel).ConfigureAwait(false) : [];
        return await detector.DetectAsync(program, packages, state, cancel).ConfigureAwait(false);
    }

    /// <summary>Installation propre à la source ; renvoie le fichier gardé pour un retour arrière et la version posée.</summary>
    private async Task<(InstallOutcome, string?, string?)> RunSourceAsync(ProgramEntry program, InstalledInfo before, string? depVersion,
                                                                          string temp, Task<Download>? ahead, IProgress<InstallStep>? progress,
                                                                          CancellationToken cancel)
    {
        if (program.IsWinget)
        {
            var blocked = before.Present ? null : program.BlockedVersion;
            Report(progress, program, L.T("Installing with winget…"));
            return (await RunWingetAsync(program, Winget.InstallArguments(program, before.Present, blocked), cancel).ConfigureAwait(false), null, null);
        }

        if (ahead is { IsCompleted: false })
            Report(progress, program, L.T("Downloading {0}…", program.Name));
        var (_, file, version) = ahead is not null
            ? await ahead.ConfigureAwait(false)
            : await FetchAsync(program, before, depVersion, temp, progress, cancel).ConfigureAwait(false);

        InstallOutcome outcome;
        if (program.Source is Sources.Chocolatey or Sources.GitHub)
            outcome = program.InstallCommand is not null
                ? await RunCommandAsync(program, InstalledInfo.Absent, file, version, depVersion, progress, cancel).ConfigureAwait(false)
                : await LaunchInstallerAsync(program, file!, progress, cancel).ConfigureAwait(false);
        else if (file is null || program.Source == Sources.Command || program.InstallCommand is not null)
            outcome = await RunCommandAsync(program, before, file, version, depVersion, progress, cancel).ConfigureAwait(false);
        else if (program.Source == Sources.Zip)
            outcome = ExtractZip(program, file, temp, progress);
        else if (program.InstallDir is not null && Path.GetExtension(file).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            outcome = CopyPortable(program, file, progress);
        else
            outcome = await LaunchInstallerAsync(program, file, progress, cancel).ConfigureAwait(false);
        return (outcome, file, version);
    }

    /// <summary>Recherche, téléchargement et contrôle du fichier d'installation (hors winget).</summary>
    private async Task<Download> FetchAsync(ProgramEntry program, InstalledInfo before, string? depVersion, string temp,
                                            IProgress<InstallStep>? progress, CancellationToken cancel)
    {
        Report(progress, program, L.T("Looking for the latest version…"));
        switch (program.Source)
        {
            case Sources.Chocolatey:
            {
                var release = await Chocolatey.GetLatestAsync(program.Package!, cancel).ConfigureAwait(false);
                Report(progress, program, L.T("Downloading {0}…", release.Version));
                var nupkg = Path.Combine(temp, $"{program.Package}.{release.Version}.nupkg");
                await Chocolatey.DownloadAsync(release, nupkg, cancel).ConfigureAwait(false);
                var installer = Chocolatey.ExtractInstaller(nupkg, program.InstallerPattern ?? @"\.(exe|msi)$", Path.Combine(temp, "installer"));
                await integrity.CheckAsync(program, installer, depVersion, cancel).ConfigureAwait(false);
                return new Download(temp, installer, release.Version);
            }
            case Sources.GitHub:
            {
                var latest = await resolver.ResolveAsync(program, [], before, cancel).ConfigureAwait(false)
                             ?? throw new InvalidOperationException(L.T("{0}: no release found.", program.Name));
                Report(progress, program, L.T("Downloading {0}…", latest.Version));
                var file = Path.Combine(temp, latest.Asset!.Name);
                using (var client = github(program.Package!))
                    await client.DownloadAssetAsync(latest.Asset, file, Fraction(progress, program), cancel).ConfigureAwait(false);
                await integrity.CheckAsync(program, file, depVersion, cancel).ConfigureAwait(false);
                return new Download(temp, file, latest.Version);
            }
        }

        var found = program.Latest is not null
            ? await resolver.ResolveAsync(program, [], before, cancel).ConfigureAwait(false)
            : null;
        var target = found?.Version ?? program.Version;
        var url = found?.DownloadUrl ?? (program.Url is { } address ? ShellCommands.Expand(address, version: target, depVersion: depVersion) : null);
        if (url is null)
            return new Download(temp, null, target);

        Report(progress, program, L.T("Downloading {0}…", target ?? program.Name));
        var downloaded = await DownloadAsync(url, temp, program.Latest?.Url, Fraction(progress, program), cancel).ConfigureAwait(false);
        await integrity.CheckAsync(program, downloaded, depVersion, cancel).ConfigureAwait(false);
        return new Download(temp, downloaded, target);
    }

    private async Task<string> DownloadAsync(string url, string temp, string? referer, IProgress<double>? progress, CancellationToken cancel)
    {
        if (!url.StartsWith(ProfileStore.ProfileScheme, StringComparison.Ordinal))
            return await Web.DownloadAsync(url, Path.Combine(temp, "download"), referer, progress, cancel).ConfigureAwait(false);
        var destination = Path.Combine(temp, Path.GetFileName(url[ProfileStore.ProfileScheme.Length..]));
        await profiles.DownloadAsync(url, destination, cancel).ConfigureAwait(false);
        return destination;
    }

    private async Task<InstallOutcome> RunWingetAsync(ProgramEntry program, string arguments, CancellationToken cancel)
    {
        Log.Info($"winget {arguments}");
        var output = await winget.RunAsync(arguments, cancel).ConfigureAwait(false);
        if (output.ExitCode == 0)
            return new InstallOutcome(InstallResult.Done);
        if (Winget.CancelledCodes.Contains(output.ExitCode))
            return new InstallOutcome(InstallResult.Cancelled, L.T("Cancelled."));
        Log.Error($"winget {Winget.ExitCodeText(output.ExitCode)} : {output.Text}");
        if (Winget.DownloadNotFoundCodes.Contains(output.ExitCode))
            return new InstallOutcome(InstallResult.Failed, L.T("{0}: installer not found at the publisher, winget package not updated yet (code {1}).", program.Name, Winget.ExitCodeText(output.ExitCode)));
        return new InstallOutcome(InstallResult.Failed, L.T("{0}: winget failed (code {1}).", program.Name, Winget.ExitCodeText(output.ExitCode)));
    }

    /// <summary>Installeur .exe ou .msi : silencieux avec ses arguments, ou ouvert à l'utilisateur (interactif).</summary>
    private async Task<InstallOutcome> LaunchInstallerAsync(ProgramEntry program, string file, IProgress<InstallStep>? progress,
                                                            CancellationToken cancel)
    {
        Report(progress, program, program.Interactive ? L.T("Installer opened, follow its steps…") : L.T("Installing…"));
        var msi = Path.GetExtension(file).Equals(".msi", StringComparison.OrdinalIgnoreCase);
        var silent = program.Interactive ? "" : program.SilentArgs ?? "";
        var start = msi
            ? new ProcessStartInfo("msiexec.exe", $"/i \"{file}\"{(program.Interactive ? "" : " /qn /norestart")} {silent}".TrimEnd())
            : new ProcessStartInfo(file, silent);
        start.UseShellExecute = true;
        start.WorkingDirectory = Path.GetDirectoryName(file)!;
        if (program.Elevate)
            start.Verb = "runas";
        int code;
        try
        {
            code = await runner.RunAsync(start, cancel).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception exc) when (exc.NativeErrorCode == 1223)
        {
            return new InstallOutcome(InstallResult.Cancelled, L.T("Cancelled."));
        }
        return Exit(program, code);
    }

    private async Task<InstallOutcome> RunCommandAsync(ProgramEntry program, InstalledInfo before, string? file, string? version,
                                                       string? depVersion, IProgress<InstallStep>? progress, CancellationToken cancel)
    {
        var template = before.Present && program.UpdateCommand is not null ? program.UpdateCommand : program.InstallCommand;
        if (template is null)
            return new InstallOutcome(InstallResult.Failed, L.T("{0}: no install command.", program.Name));
        var command = ShellCommands.Expand(template, file, version, depVersion);
        Report(progress, program, L.T("Installing…"));
        Log.Info($"Commande {(program.Elevate ? "administrateur " : "")}: {command}");
        if (program.Elevate)
            return Exit(program, await shell.RunElevatedAsync(command, cancel).ConfigureAwait(false));
        var output = await shell.RunHiddenAsync(command, cancel).ConfigureAwait(false);
        if (output.ExitCode != 0)
            Log.Error($"{program.Id} : {output.Text}");
        return Exit(program, output.ExitCode);
    }

    private static InstallOutcome Exit(ProgramEntry program, int code) => code switch
    {
        0 or 3010 or 1641 => new InstallOutcome(InstallResult.Done),
        1223 or 1602 => new InstallOutcome(InstallResult.Cancelled, L.T("Cancelled.")),
        _ => new InstallOutcome(InstallResult.Failed, L.T("{0}: installer ended with code {1}.", program.Name, code)),
    };

    /// <summary>
    /// Outil portable livré en zip : décompressé à part, chaque exe contrôlé (signature), puis copié par-dessus le
    /// dossier d'installation (les fichiers propres à l'utilisateur sont gardés).
    /// </summary>
    private InstallOutcome ExtractZip(ProgramEntry program, string zip, string temp, IProgress<InstallStep>? progress)
    {
        Report(progress, program, L.T("Installing…"));
        var extracted = Path.Combine(temp, "zip");
        ZipFile.ExtractToDirectory(zip, extracted, overwriteFiles: true);
        if (program.VerifyOrDefault == Verifications.Signature)
            foreach (var exe in Directory.EnumerateFiles(extracted, "*.exe", SearchOption.AllDirectories))
                integrity.CheckSignature(program, exe);
        var target = Environment.ExpandEnvironmentVariables(program.InstallDir!);
        foreach (var source in Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(target, Path.GetRelativePath(extracted, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
        return new InstallOutcome(InstallResult.Done);
    }

    private static InstallOutcome CopyPortable(ProgramEntry program, string exe, IProgress<InstallStep>? progress)
    {
        Report(progress, program, L.T("Installing…"));
        var target = Environment.ExpandEnvironmentVariables(program.InstallDir!);
        Directory.CreateDirectory(target);
        File.Copy(exe, Path.Combine(target, Path.GetFileName(exe)), overwrite: true);
        return new InstallOutcome(InstallResult.Done);
    }

    /// <summary>Vrai si un retour à la version précédente est possible pour ce logiciel.</summary>
    public bool CanRevert(ProgramEntry program) =>
        cache.PreviousOf(program.Id) is { } previous && (program.IsWinget || previous.File is not null && File.Exists(previous.File));

    /// <summary>Réinstalle la version précédente gardée (winget : version imposée ; sinon l'installeur gardé).</summary>
    public async Task<InstallOutcome> RevertAsync(ProgramEntry program, IProgress<InstallStep>? progress = null, CancellationToken cancel = default)
    {
        if (cache.PreviousOf(program.Id) is not { } previous)
            return new InstallOutcome(InstallResult.Failed, L.T("{0}: no previous version kept.", program.Name));
        IReadOnlyList<string> closed = [];
        try
        {
            if (program.Close is { Count: > 0 } names)
                closed = processes.Close(names);
            Report(progress, program, L.T("Back to version {0}…", previous.Version));
            InstallOutcome outcome;
            if (program.IsWinget)
                outcome = await RunWingetAsync(program, Winget.InstallArguments(program, upgrade: true, previous.Version), cancel).ConfigureAwait(false);
            else if (program.Source == Sources.Zip)
            {
                var temp = Directory.CreateTempSubdirectory("UtilitiesManager-").FullName;
                outcome = ExtractZip(program, previous.File!, temp, progress);
                TryDelete(temp);
            }
            else if (program.InstallCommand is not null)
                outcome = await RunCommandAsync(program, InstalledInfo.Absent, previous.File, previous.Version, null, progress, cancel).ConfigureAwait(false);
            else if (program.InstallDir is not null && previous.File!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                outcome = CopyPortable(program, previous.File, progress);
            else
                outcome = await LaunchInstallerAsync(program, previous.File!, progress, cancel).ConfigureAwait(false);
            if (outcome.Succeeded)
            {
                cache.Reverted(program.Id);
                state.InstalledVersions[program.Id] = previous.Version;
                Log.Info($"{program.Name} revenu à la version {previous.Version}");
            }
            return outcome with { Version = previous.Version };
        }
        catch (Exception exc) when (exc is not OperationCanceledException)
        {
            Log.Error($"{program.Id} : retour arrière : {exc}");
            return new InstallOutcome(InstallResult.Failed, exc.Message);
        }
        finally
        {
            foreach (var exe in closed)
                processes.Start(exe);
        }
    }

    /// <summary>Désinstalle un logiciel (retiré du profil) : winget, commande du profil, ou commande inscrite par Windows.</summary>
    public async Task<InstallOutcome> UninstallAsync(ProgramEntry program, InstalledInfo installed, IProgress<InstallStep>? progress = null,
                                                     CancellationToken cancel = default)
    {
        Report(progress, program, L.T("Uninstalling…"));
        try
        {
            if (program.Close is { Count: > 0 } names)
                processes.Close(names);
            InstallOutcome outcome;
            if (program.IsWinget)
                outcome = await RunWingetAsync(program, Winget.UninstallArguments(program), cancel).ConfigureAwait(false);
            else if (program.UninstallCommand is { } command)
                outcome = await RunUninstallCommandAsync(program, ShellCommands.Expand(command), cancel).ConfigureAwait(false);
            else if (installed.UninstallString is { } registered)
                outcome = await RunUninstallCommandAsync(program, QuietUninstall(registered), cancel).ConfigureAwait(false);
            else if (program.InstallDir is { } folder)
            {
                Directory.Delete(Environment.ExpandEnvironmentVariables(folder), recursive: true);
                outcome = new InstallOutcome(InstallResult.Done);
            }
            else
                outcome = new InstallOutcome(InstallResult.Failed, L.T("{0}: no way to uninstall it.", program.Name));
            if (outcome.Succeeded)
            {
                state.Forget(program.Id);
                cache.Forget(program.Id);
                Log.Info($"{program.Name} désinstallé");
            }
            return outcome;
        }
        catch (Exception exc) when (exc is not OperationCanceledException)
        {
            Log.Error($"{program.Id} : désinstallation : {exc}");
            return new InstallOutcome(InstallResult.Failed, exc.Message);
        }
    }

    private async Task<InstallOutcome> RunUninstallCommandAsync(ProgramEntry program, string command, CancellationToken cancel) =>
        Exit(program, await shell.RunElevatedAsync(command, cancel).ConfigureAwait(false));

    /// <summary><c>MsiExec.exe /I{GUID}</c> (réparation) devient <c>/X{GUID} /qn</c> (désinstallation silencieuse).</summary>
    public static string QuietUninstall(string registered)
    {
        var match = System.Text.RegularExpressions.Regex.Match(registered, @"msiexec(\.exe)?\s+/[IX]\s*(\{[0-9A-Fa-f-]+\})",
                                                               System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? $"msiexec.exe /X{match.Groups[2].Value} /qn /norestart" : registered;
    }

    private static void Report(IProgress<InstallStep>? progress, ProgramEntry program, string text)
    {
        progress?.Report(new InstallStep(program.Name, text));
    }

    private static IProgress<double>? Fraction(IProgress<InstallStep>? progress, ProgramEntry program) =>
        progress is null ? null : new Progress<double>(f => progress.Report(new InstallStep(program.Name, L.T("Downloading…"), f)));

    internal static void TryDelete(string folder)
    {
        if (!Directory.Exists(folder))
            return;
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception exc) when (exc is IOException or UnauthorizedAccessException)
        {
            Log.Error($"Dossier temporaire {folder} : {exc.Message}");
        }
    }
}
