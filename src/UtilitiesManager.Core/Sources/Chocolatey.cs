using System.IO.Compression;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace UtilitiesManager.Software;

/// <summary>Dernière version d'un paquet Chocolatey et adresse de son <c>.nupkg</c>.</summary>
public sealed record ChocolateyRelease(string Version, string PackageUrl);

/// <summary>Paquet trouvé par une recherche. <see cref="Size"/> : taille du <c>.nupkg</c> en octets.</summary>
public sealed record ChocolateyPackage(string Id, string Title, string Version, long Size)
{
    /// <summary>
    /// Paquet qui embarque l'installeur (seul cas utilisable). Un paquet de quelques Ko ne contient qu'un script qui
    /// télécharge l'installeur sur le site de l'éditeur.
    /// </summary>
    public bool EmbedsInstaller => Size > 1_000_000;
}

/// <summary>
/// Paquets communautaires Chocolatey, qui embarquent les installeurs officiels (cas d'un éditeur qui interdit le
/// téléchargement par script sur son site, comme FileZilla). Seules les adresses de chocolatey.org sont appelées.
/// </summary>
public static class Chocolatey
{
    private const string ApiUrl = "https://community.chocolatey.org/api/v2/package/";
    private const string SearchUrl = "https://community.chocolatey.org/api/v2/Search()";

    private static readonly HttpClient NoRedirect = CreateClient(allowRedirect: false);
    private static readonly HttpClient Http = CreateClient(allowRedirect: true);

    private static HttpClient CreateClient(bool allowRedirect)
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = allowRedirect })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppPaths.AppName}/{ViewModels.AppInfo.Version}");
        return client;
    }

    public static string LatestUrl(string package) => ApiUrl + Uri.EscapeDataString(package);

    /// <summary>Vrai pour chocolatey.org et ses sous-domaines, en HTTPS.</summary>
    public static bool IsChocolatey(Uri address) =>
        address.Scheme == Uri.UriSchemeHttps
        && (address.Host.Equals("chocolatey.org", StringComparison.OrdinalIgnoreCase)
            || address.Host.EndsWith(".chocolatey.org", StringComparison.OrdinalIgnoreCase));

    /// <summary>Version et adresse du paquet, lues dans l'adresse de redirection <c>paquet.version.nupkg</c>.</summary>
    public static ChocolateyRelease ParseRedirect(string package, Uri? location)
    {
        if (location is null)
            throw new InvalidDataException(L.T("Chocolatey did not give the address of the package."));
        if (!location.IsAbsoluteUri)
            location = new Uri(new Uri(LatestUrl(package)), location);
        var match = Regex.Match(Path.GetFileName(location.AbsolutePath),
                                "^" + Regex.Escape(package) + @"\.(\d+(?:\.\d+){1,3})\.nupkg$", RegexOptions.IgnoreCase);
        if (!IsChocolatey(location) || !match.Success)
            throw new InvalidDataException(L.T("Unexpected package address: {0}", location));
        return new ChocolateyRelease(match.Groups[1].Value, location.AbsoluteUri);
    }

    /// <summary>Requête HEAD, sans suivre la redirection : rien n'est téléchargé.</summary>
    public static async Task<ChocolateyRelease> GetLatestAsync(string package, CancellationToken cancel = default)
    {
        using var timeout = Limit(cancel, TimeSpan.FromSeconds(30));
        using var request = new HttpRequestMessage(HttpMethod.Head, LatestUrl(package));
        using var response = await NoRedirect.SendAsync(request, timeout.Token);
        var status = (int)response.StatusCode;
        if (status is >= 300 and < 400)
            return ParseRedirect(package, response.Headers.Location);
        throw new HttpRequestException(response.StatusCode == HttpStatusCode.TooManyRequests
            ? L.T("Chocolatey limits the number of requests, try again later.")
            : L.T("Unexpected answer from Chocolatey (HTTP {0}).", status), null, response.StatusCode);
    }

    /// <summary>Paquets dont le nom ou la description contient <paramref name="query"/> (dernières versions).</summary>
    public static async Task<IReadOnlyList<ChocolateyPackage>> SearchAsync(string query, CancellationToken cancel = default)
    {
        using var timeout = Limit(cancel, TimeSpan.FromSeconds(30));
        var term = Uri.EscapeDataString("'" + query.Replace("'", "''") + "'");
        var url = $"{SearchUrl}?$filter=IsLatestVersion&$top=10&searchTerm={term}&targetFramework=''&includePrerelease=false";
        return ParseSearch(await Http.GetStringAsync(url, timeout.Token));
    }

    /// <summary>Lecture du flux Atom de la recherche OData.</summary>
    public static IReadOnlyList<ChocolateyPackage> ParseSearch(string xml)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        XNamespace data = "http://schemas.microsoft.com/ado/2007/08/dataservices";
        XNamespace meta = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";
        return XDocument.Parse(xml).Root!.Elements(atom + "entry").Select(entry =>
        {
            var properties = entry.Element(meta + "properties");
            string? Read(string name) => properties?.Element(data + name)?.Value;
            var id = entry.Element(atom + "title")?.Value ?? "";
            return new ChocolateyPackage(id, Read("Title") is { Length: > 0 } title ? title : id, Read("Version") ?? "",
                                         long.TryParse(Read("PackageSize"), out var size) ? size : 0);
        }).Where(p => p.Id.Length > 0).ToList();
    }

    public static async Task DownloadAsync(ChocolateyRelease release, string destination, CancellationToken cancel = default)
    {
        var address = new Uri(release.PackageUrl);
        if (!IsChocolatey(address))
            throw new InvalidDataException(L.T("Unexpected package address: {0}", address));
        using var timeout = Limit(cancel, TimeSpan.FromMinutes(10));
        using var response = await Http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } final && !IsChocolatey(final))
            throw new InvalidDataException(L.T("Unexpected package address: {0}", final));
        await using var file = File.Create(destination);
        await response.Content.CopyToAsync(file, timeout.Token);
    }

    /// <summary>
    /// Extrait du paquet (un zip) le premier fichier dont le chemin correspond à <paramref name="pattern"/> et
    /// renvoie son chemin dans <paramref name="folder"/>.
    /// </summary>
    public static string ExtractInstaller(string package, string pattern, string folder)
    {
        using var zip = ZipFile.OpenRead(package);
        var entry = zip.Entries.FirstOrDefault(e => Regex.IsMatch(e.FullName.Replace('\\', '/'), pattern, RegexOptions.IgnoreCase))
                    ?? throw new InvalidDataException(L.T("The package does not contain the installer {0}.", pattern));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, entry.Name);
        entry.ExtractToFile(path, overwrite: true);
        return path;
    }

    private static CancellationTokenSource Limit(CancellationToken cancel, TimeSpan delay)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        source.CancelAfter(delay);
        return source;
    }
}
