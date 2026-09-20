using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace SysWlan.Core;

public sealed record RouterConnectionTestResult(bool Succeeded, string Message, int? StatusCode = null);

public sealed class RouterConnectionTester : IDisposable
{
    private readonly HttpClient client;
    private readonly TimeSpan timeout;

    public RouterConnectionTester() : this(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false }, TimeSpan.FromSeconds(5)) { }

    public RouterConnectionTester(HttpMessageHandler handler, TimeSpan timeout)
    {
        client = new HttpClient(handler) { Timeout = timeout };
        this.timeout = timeout;
    }

    public async Task<RouterConnectionTestResult> TestAsync(string? gateway, string username, string password, bool https, CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(gateway, out var address))
            return new(false, "Kein gültiges Router-Gateway vorhanden.");
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return new(false, "Benutzername und Passwort müssen eingegeben werden.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, new UriBuilder(https ? "https" : "http", address.ToString()).Uri);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            return response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => new(false, "Der Router hat diese Zugangsdaten abgelehnt.", (int)response.StatusCode),
                _ when (int)response.StatusCode is >= 200 and < 400 => new(true, "Der Router hat die Zugangsdaten akzeptiert.", (int)response.StatusCode),
                _ => new(false, $"Der Router antwortet mit HTTP {(int)response.StatusCode}.", (int)response.StatusCode)
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(false, "Der Verbindungstest wurde wegen Zeitüberschreitung beendet.");
        }
        catch (HttpRequestException)
        {
            return new(false, "Der Router war für den Verbindungstest nicht erreichbar.");
        }
    }

    public void Dispose() => client.Dispose();
}
