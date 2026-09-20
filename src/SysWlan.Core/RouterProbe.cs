using System.Net;
using System.Text;

namespace SysWlan.Core;

public sealed class RouterProbe : IDisposable
{
    private readonly HttpClient client;
    private readonly TimeSpan timeout;
    public RouterProbe() : this(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false }, TimeSpan.FromSeconds(4)) { }
    public RouterProbe(HttpMessageHandler handler, TimeSpan timeout)
    {
        this.timeout = timeout; client = new HttpClient(handler) { Timeout = timeout };
    }
    public async Task<RouterStatus> ReadAsync(string? gateway, CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(gateway, out var address)) return new() { Error = "Kein Gateway vorhanden." };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var token = deadline.Token;
        try
        {
            using var response = await client.GetAsync(new UriBuilder("http", address.ToString()).Uri, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) return new() { Error = $"Router meldet HTTP {(int)response.StatusCode}; Anmeldung oder andere Verwaltungsadresse erforderlich.", CheckedAt = DateTimeOffset.UtcNow };
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var memory = new MemoryStream(); var buffer = new byte[8192]; int bytes;
            while ((bytes = await stream.ReadAsync(buffer, token)) != 0)
            {
                if (memory.Length + bytes > 1_048_576) throw new IOException("Routerantwort überschreitet 1 MiB.");
                memory.Write(buffer, 0, bytes);
            }
            return RouterParser.Parse(Encoding.UTF8.GetString(memory.ToArray()));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new() { Error = "Öffentlicher Routerstatus nicht erreichbar.", CheckedAt = DateTimeOffset.UtcNow };
        }
    }
    public void Dispose() => client.Dispose();
}
