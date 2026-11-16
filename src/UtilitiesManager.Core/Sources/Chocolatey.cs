using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;

namespace UtilitiesManager;

/// <summary>Dernière version de FileZilla publiée dans le paquet Chocolatey.</summary>
/// <param name="PackageUrl">Adresse du paquet <c>filezilla.&lt;version&gt;.nupkg</c>.</param>
public sealed record FileZillaRelease(Version Version, string PackageUrl)
{
    public string PackageFileName => Path.GetFileName(new Uri(PackageUrl).AbsolutePath);
}

/// <summary>Source des versions et des paquets de FileZilla, remplacée par un faux dans les tests.</summary>
public interface IFileZillaSource
{
    /// <summary>
    /// Dernière version publiée. <see cref="HttpRequestException"/> sans code HTTP : réseau
    /// absent (nouvel essai possible) ; avec un code ou <see cref="InvalidDataException"/> :
    /// réponse inattendue (pas de nouvel essai).
    /// </summary>
    Task<FileZillaRelease> GetLatestAsync(CancellationToken cancel = default);

    Task DownloadAsync(FileZillaRelease release, string destination, CancellationToken cancel = default);
}

/// <summary>
/// Paquet communautaire Chocolatey <c>filezilla</c>, qui embarque les installeurs
/// officiels. L'auteur de FileZilla interdit aux outils automatiques de télécharger sur
/// ses serveurs : aucune adresse de filezilla-project.org n'est appelée, seules les
/// adresses de chocolatey.org sont acceptées.
/// </summary>
public sealed partial class ChocolateySource : IFileZillaSource
{
    /// <summary>Répond par une redirection vers <c>https://packages.chocolatey.org/filezilla.&lt;version&gt;.nupkg</c>.</summary>
    public const string LatestUrl = "https://community.chocolatey.org/api/v2/package/filezilla";

    private static readonly HttpClient NoRedirect = CreateClient(allowRedirect: false);
    private static readonly HttpClient Http = CreateClient(allowRedirect: true);

    private static HttpClient CreateClient(bool allowRedirect)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = allowRedirect })
        {
            Timeout = System.Threading.Timeout.InfiniteTimeSpan,
        };
        var version = typeof(ChocolateySource).Assembly.GetName().Version!;
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppPaths.AppName}/{version.ToString(3)}");
        return client;
    }

    [GeneratedRegex(@"^filezilla\.(\d+(?:\.\d+){1,3})\.nupkg$", RegexOptions.IgnoreCase)]
    private static partial Regex PackageName();

    /// <summary>Vrai pour chocolatey.org et ses sous-domaines.</summary>
    public static bool IsChocolatey(Uri address) =>
        address.Scheme == Uri.UriSchemeHttps
        && (address.Host.Equals("chocolatey.org", StringComparison.OrdinalIgnoreCase)
            || address.Host.EndsWith(".chocolatey.org", StringComparison.OrdinalIgnoreCase));

    /// <summary>Version et adresse du paquet, lues dans l'adresse de redirection.</summary>
    public static FileZillaRelease ParseRedirect(Uri? location)
    {
        if (location is null)
            throw new InvalidDataException(L.T("Chocolatey did not give the address of the package."));
        if (!location.IsAbsoluteUri)
            location = new Uri(new Uri(LatestUrl), location);
        var match = PackageName().Match(Path.GetFileName(location.AbsolutePath));
        if (!IsChocolatey(location) || !match.Success)
            throw new InvalidDataException(L.T("Unexpected package address: {0}", location));
        return new FileZillaRelease(VersionText.Parse(match.Groups[1].Value)!, location.AbsoluteUri);
    }

    /// <summary>Requête HEAD, sans suivre la redirection : rien n'est téléchargé.</summary>
    public async Task<FileZillaRelease> GetLatestAsync(CancellationToken cancel = default)
    {
        using var timeout = Limit(cancel, TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Head, LatestUrl);
        using var response = await NoRedirect.SendAsync(request, timeout.Token);
        var status = (int)response.StatusCode;
        if (status is >= 300 and < 400)
            return ParseRedirect(response.Headers.Location);
        throw new HttpRequestException(response.StatusCode == HttpStatusCode.TooManyRequests
            ? L.T("Chocolatey limits the number of requests, try again later.")
            : L.T("Unexpected answer from Chocolatey (HTTP {0}).", status), null, response.StatusCode);
    }

    public async Task DownloadAsync(FileZillaRelease release, string destination, CancellationToken cancel = default)
    {
        var address = new Uri(release.PackageUrl);
        if (!IsChocolatey(address))
            throw new InvalidDataException(L.T("Unexpected package address: {0}", address));
        // Délai propre au téléchargement : le paquet pèse environ 25 Mo.
        using var timeout = Limit(cancel, TimeSpan.FromMinutes(10));
        using var response = await Http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } final && !IsChocolatey(final))
            throw new InvalidDataException(L.T("Unexpected package address: {0}", final));
        await using var file = File.Create(destination);
        await response.Content.CopyToAsync(file, timeout.Token);
    }

    private static CancellationTokenSource Limit(CancellationToken cancel, TimeSpan delay)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        source.CancelAfter(delay);
        return source;
    }
}

/// <summary>Contenu du paquet Chocolatey : un zip qui contient <c>tools/FileZilla_&lt;version&gt;_win64-setup.exe</c>.</summary>
public static partial class FileZillaPackage
{
    [GeneratedRegex(@"^tools/FileZilla_(\d+(?:\.\d+){1,3})_win64-setup\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex Installer64();

    /// <summary>
    /// Extrait l'installeur 64 bits dans <paramref name="folder"/> et renvoie son chemin.
    /// Lève <see cref="InvalidDataException"/> s'il manque ou si sa version n'est pas
    /// <paramref name="expected"/>.
    /// </summary>
    public static string ExtractInstaller(string package, Version expected, string folder)
    {
        using var zip = ZipFile.OpenRead(package);
        foreach (var entry in zip.Entries)
        {
            var match = Installer64().Match(entry.FullName.Replace('\\', '/'));
            if (!match.Success)
                continue;
            if (VersionText.Parse(match.Groups[1].Value) != expected)
                throw new InvalidDataException(L.T("The package contains {0} instead of version {1}.",
                                                   entry.Name, VersionText.Display(expected)));
            var path = Path.Combine(folder, entry.Name);
            entry.ExtractToFile(path, overwrite: true);
            return path;
        }
        throw new InvalidDataException(L.T("The package does not contain the 64-bit FileZilla installer."));
    }
}
