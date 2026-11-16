namespace UtilitiesManager.Software;

/// <summary>
/// Pages et fichiers des éditeurs. Certains sites refusent un client qui ne ressemble pas à un navigateur ou qui
/// n'indique pas la page d'origine : en-têtes d'un navigateur, page lue en référent.
/// </summary>
public static class Web
{
    private const string BrowserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(BrowserAgent);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        return client;
    }

    private static HttpRequestMessage Request(string url, string? referer)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (referer is not null)
            request.Headers.Referrer = new Uri(referer);
        return request;
    }

    public static async Task<string> GetStringAsync(string url, CancellationToken cancel = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        using var request = Request(url, url);
        using var response = await Http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Télécharge <paramref name="url"/> dans le dossier <paramref name="folder"/>, sous le nom donné par le serveur
    /// ou l'adresse finale ; renvoie le chemin du fichier.
    /// </summary>
    public static async Task<string> DownloadAsync(string url, string folder, string? referer = null, IProgress<double>? progress = null,
                                                   CancellationToken cancel = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(TimeSpan.FromMinutes(30));
        using var request = Request(url, referer);
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var name = response.Content.Headers.ContentDisposition?.FileNameStar?.Trim('"')
                   ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        if (string.IsNullOrWhiteSpace(name))
            name = Uri.UnescapeDataString(Path.GetFileName((response.RequestMessage?.RequestUri ?? new Uri(url)).AbsolutePath));
        name = string.Concat(Path.GetFileName(name).Split(Path.GetInvalidFileNameChars()));
        if (name.Length == 0)
            name = "download.bin";
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        var total = response.Content.Headers.ContentLength ?? 0;
        await using (var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
        await using (var target = File.Create(path))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                done += read;
                if (total > 0)
                    progress?.Report((double)done / total);
            }
        }
        return path;
    }
}
