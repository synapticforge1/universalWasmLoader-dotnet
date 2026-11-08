using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UniversalWasmLoader.Tests;

/// <summary>
/// A minimal HTTP/1.1 server on 127.0.0.1 for the SPEC §8 http refusal rows. Each path answers a
/// fixed status and body, or STALLS (accepts the request and never answers). It counts requests per
/// path (query dropped) and records every request target, so "one request per file" and "the
/// <c>.wasm</c> is never fetched" are checked from its counts.
/// </summary>
internal sealed class TestHttpServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentDictionary<string, (int Status, byte[] Body, bool Stall)> _routes = new();
    private readonly ConcurrentDictionary<string, int> _counts = new();

    public ConcurrentQueue<string> Targets { get; } = new();

    public TestHttpServer()
    {
        _listener.Start();
        _ = AcceptLoop();
    }

    public string Url(string path) => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{path}";

    public TestHttpServer Serve(string path, int status, byte[]? body = null)
    {
        _routes[path] = (status, body ?? Array.Empty<byte>(), false);
        return this;
    }

    public TestHttpServer ServeFile(string path, string file) => Serve(path, 200, File.ReadAllBytes(file));

    public TestHttpServer Stall(string path)
    {
        _routes[path] = (0, Array.Empty<byte>(), true);
        return this;
    }

    public int Count(string path) => _counts.TryGetValue(path, out var n) ? n : 0;

    private async Task AcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
            catch { return; }
            _ = Handle(client);
        }
    }

    private async Task Handle(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var head = new StringBuilder();
                var buf = new byte[1];
                while (!head.ToString().EndsWith("\r\n\r\n"))
                {
                    if (await stream.ReadAsync(buf, _stop.Token) == 0) return;
                    head.Append((char)buf[0]);
                }
                var target = head.ToString().Split(' ')[1];
                Targets.Enqueue(target);
                var path = target.Split('?', '#')[0];
                _counts.AddOrUpdate(path, 1, (_, n) => n + 1);

                if (!_routes.TryGetValue(path, out var route)) route = (404, Array.Empty<byte>(), false);
                if (route.Stall)
                {
                    await Task.Delay(Timeout.Infinite, _stop.Token);
                    return;
                }
                var header = Encoding.ASCII.GetBytes(
                    $"HTTP/1.1 {route.Status} Status\r\nContent-Length: {route.Body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, _stop.Token);
                await stream.WriteAsync(route.Body, _stop.Token);
            }
            catch
            {
                // the server is stopping, or the client gave up (a timeout test)
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
    }
}
