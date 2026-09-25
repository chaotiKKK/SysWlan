using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SysWlan.Core;

public sealed class SyslogReceiver(Store store) : IDisposable
{
    private UdpClient? client;
    private CancellationTokenSource? cancellation;
    public string? Endpoint { get; private set; }
    /// <summary>Meldet jede angenommene Nachricht nach der Speicherung, z. B. für die Gerätequelle per Router-Syslog.</summary>
    public event Action<LogEntry>? Received;
    public string? Error { get; private set; }
    public bool Running => client is not null;
    public void Start(string bindAddress, int port)
    {
        if (port is < 1024 or > 65535) throw new ArgumentException("Port muss zwischen 1024 und 65535 liegen.");
        var address = IPAddress.Parse(bindAddress);
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) throw new ArgumentException("Eine konkrete lokale IP-Adresse auswählen.");
        Stop(); var socket = new UdpClient(new IPEndPoint(address, port));
        client = socket; cancellation = new(); Endpoint = $"{address}:{port}"; Error = null;
        _ = ReceiveAsync(socket, cancellation.Token);
        store.AddLog(new(DateTimeOffset.UtcNow, "App", "Info", $"Syslog-Empfang auf {Endpoint} gestartet; Versand muss im Router eingerichtet sein."));
    }
    private async Task ReceiveAsync(UdpClient socket, CancellationToken token)
    {
        var window = DateTimeOffset.UtcNow; var count = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var packet = await socket.ReceiveAsync(token);
                if ((DateTimeOffset.UtcNow - window).TotalSeconds >= 1) { window = DateTimeOffset.UtcNow; count = 0; }
                if (++count > 50) continue;
                var entry = SyslogParser.Parse(Encoding.UTF8.GetString(packet.Buffer, 0, Math.Min(packet.Buffer.Length, 8192)), packet.RemoteEndPoint.Address.ToString(), DateTimeOffset.UtcNow);
                store.AddLog(entry);
                Received?.Invoke(entry);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (token.IsCancellationRequested) { }
        catch (Exception ex) { Error = SyslogParser.Redact(ex.Message); if (ReferenceEquals(client, socket)) Stop(); }
    }
    public void Stop() { cancellation?.Cancel(); client?.Dispose(); client = null; cancellation?.Dispose(); cancellation = null; Endpoint = null; }
    public void Dispose() => Stop();
}
