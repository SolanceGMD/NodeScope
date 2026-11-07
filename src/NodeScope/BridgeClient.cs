using System.Net.Sockets;
using System.Text;
using System.Text.Json;
namespace NodeScope;

public sealed class BridgeClient : IAsyncDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _sequence;
    public bool Connected => _client?.Connected == true;

    public async Task ConnectAsync(string host = "127.0.0.1", int port = 47831, CancellationToken ct = default)
    {
        await DisconnectAsync();
        var client = new TcpClient();
        await client.ConnectAsync(host, port, ct);
        _client = client;
        _stream = client.GetStream();
        await SendAsync("ping", new { }, ct);
    }

    public async Task<JsonElement> SendAsync(string action, object args, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_stream is null) throw new InvalidOperationException("Not connected to a NodeScope-enabled game.");
            int id = Interlocked.Increment(ref _sequence);
            var payload = JsonSerializer.Serialize(new { id, action, args }) + "\n";
            await _stream.WriteAsync(Encoding.UTF8.GetBytes(payload), ct);
            using var ms = new MemoryStream();
            var one = new byte[1];
            while (true)
            {
                int n = await _stream.ReadAsync(one, ct);
                if (n == 0) throw new IOException("Game disconnected.");
                if (one[0] == 10) break;
                ms.WriteByte(one[0]);
                if (ms.Length > 4_000_000) throw new IOException("Bridge response too large.");
            }
            using var doc = JsonDocument.Parse(ms.ToArray());
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok) && !ok.GetBoolean())
                throw new InvalidOperationException(root.GetProperty("result").GetProperty("error").GetString());
            return root.GetProperty("result").Clone();
        }
        finally { _gate.Release(); }
    }

    public async Task DisconnectAsync()
    {
        _stream?.Dispose(); _stream = null;
        _client?.Dispose(); _client = null;
        await Task.CompletedTask;
    }
    public async ValueTask DisposeAsync() { await DisconnectAsync(); _gate.Dispose(); }
}
