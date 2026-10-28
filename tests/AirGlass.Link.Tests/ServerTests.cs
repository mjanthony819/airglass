using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using AirGlass.Core.Link;
using AirGlass.Link;
using Xunit;

namespace AirGlass.Link.Tests;

public sealed class Fixture : IAsyncLifetime
{
    public readonly List<InputMessage> Inputs = new();
    public string? Clip;
    public PairingService Pairing = new();
    public LinkServer Server = null!;
    public string Dir = Path.Combine(Path.GetTempPath(), "agl-" + Guid.NewGuid().ToString("N"));
    public int Port;
    public HttpClient Http = null!;
    public string Base => $"https://127.0.0.1:{Port}";

    sealed class Sink(Fixture f) : IInputSink, IClipboardSink
    {
        public void Apply(InputMessage m) { lock (f.Inputs) f.Inputs.Add(m); }
        public void SetText(string t) => f.Clip = t;
        public string? GetText() => f.Clip;
    }

    public async Task InitializeAsync()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var tmp = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
        var cert = new X509Certificate2(tmp.Export(X509ContentType.Pfx));
        var l = new TcpListener(IPAddress.Loopback, 0); l.Start(); Port = ((IPEndPoint)l.LocalEndpoint).Port; l.Stop();
        var s = new Sink(this);
        Server = new LinkServer(Pairing, s, s, Path.Combine(Dir, "in"), Path.Combine(Dir, "out"));
        await Server.StartAsync(cert, Port);
        Http = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => true });
    }

    public async Task DisposeAsync()
    {
        await Server.DisposeAsync();
        Http.Dispose();
        try { Directory.Delete(Dir, true); } catch { }
    }

    public async Task<string> PairAsync()
    {
        var code = Pairing.NewCode();
        var r = await Http.PostAsJsonAsync(Base + "/api/pair", new { code, name = "Test phone" });
        r.EnsureSuccessStatusCode();
        var id = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        Assert.True(Pairing.Approve(id));
        var p = await Http.GetFromJsonAsync<JsonElement>(Base + "/api/pair/" + id);
        return p.GetProperty("token").GetString()!;
    }

    public HttpRequestMessage Req(HttpMethod m, string path, string? token, HttpContent? c = null)
    {
        var r = new HttpRequestMessage(m, Base + path) { Content = c };
        if (token != null) r.Headers.Authorization = new("Bearer", token);
        return r;
    }
}

public class ServerTests : IClassFixture<Fixture>
{
    private readonly Fixture f;
    public ServerTests(Fixture fx) => f = fx;

    [Fact]
    public async Task Serves_phone_page_over_https_with_security_headers()
    {
        var r = await f.Http.GetAsync(f.Base + "/");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("AirGlass Link", await r.Content.ReadAsStringAsync());
        Assert.Contains("frame-ancestors 'none'", r.Headers.GetValues("Content-Security-Policy").First());
        Assert.Equal("no-store", r.Headers.GetValues("Cache-Control").First());
    }

    [Fact]
    public async Task Plain_http_is_not_served()
    {
        using var h = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        await Assert.ThrowsAnyAsync<Exception>(async () => await h.GetStringAsync($"http://127.0.0.1:{f.Port}/"));
    }

    [Fact]
    public async Task Protected_endpoints_reject_missing_or_bad_tokens()
    {
        foreach (var p in new[] { "/api/me", "/api/files", "/api/clip", "/api/files/x.txt" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.Http.SendAsync(f.Req(HttpMethod.Get, p, null))).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await f.Http.SendAsync(f.Req(HttpMethod.Get, p, "bogus"))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.Http.SendAsync(f.Req(HttpMethod.Post, "/api/files", "bogus", new StringContent("x")))).StatusCode);
    }

    [Fact]
    public async Task Wrong_pairing_code_is_rejected()
    {
        f.Pairing.NewCode();
        var r = await f.Http.PostAsJsonAsync(f.Base + "/api/pair", new { code = "abcdef", name = "x" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Cross_origin_requests_are_refused()
    {
        var code = f.Pairing.NewCode();
        var r = new HttpRequestMessage(HttpMethod.Post, f.Base + "/api/pair") { Content = JsonContent.Create(new { code, name = "x" }) };
        r.Headers.Add("Origin", "https://evil.example");
        Assert.Equal(HttpStatusCode.Forbidden, (await f.Http.SendAsync(r)).StatusCode);
        f.Pairing.CancelCode();
    }

    [Fact]
    public async Task File_upload_download_and_traversal_defence()
    {
        var tok = await f.PairAsync();
        var up = f.Req(HttpMethod.Post, "/api/files", tok, new ByteArrayContent(Encoding.UTF8.GetBytes("hello")));
        up.Headers.Add("X-Filename", Uri.EscapeDataString("..\\..\\evil.exe"));
        var r = await f.Http.SendAsync(up);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var name = (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString()!;
        Assert.Equal("evil.exe.blocked", name);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(f.Dir, "in", name)));

        File.WriteAllText(Path.Combine(f.Dir, "out", "note.txt"), "from pc");
        var list = await (await f.Http.SendAsync(f.Req(HttpMethod.Get, "/api/files", tok))).Content.ReadAsStringAsync();
        Assert.Contains("note.txt", list);
        var dl = await f.Http.SendAsync(f.Req(HttpMethod.Get, "/api/files/note.txt", tok));
        Assert.Equal("from pc", await dl.Content.ReadAsStringAsync());
        var bad = await f.Http.SendAsync(f.Req(HttpMethod.Get, "/api/files/..%2f..%2fin%2f" + name, tok));
        Assert.Equal(HttpStatusCode.NotFound, bad.StatusCode);
    }

    [Fact]
    public async Task Clipboard_round_trip_needs_token()
    {
        var tok = await f.PairAsync();
        var r = await f.Http.SendAsync(f.Req(HttpMethod.Post, "/api/clip", tok, new StringContent("copied")));
        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal("copied", f.Clip);
        Assert.Equal("copied", await (await f.Http.SendAsync(f.Req(HttpMethod.Get, "/api/clip", tok))).Content.ReadAsStringAsync());
    }

    private async Task<ClientWebSocket> Ws()
    {
        var ws = new ClientWebSocket();
        ws.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        await ws.ConnectAsync(new Uri($"wss://127.0.0.1:{f.Port}/ws"), CancellationToken.None);
        return ws;
    }

    private static Task Send(ClientWebSocket ws, string s) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(s), WebSocketMessageType.Text, true, CancellationToken.None);

    [Fact]
    public async Task WebSocket_input_only_applies_after_token_auth_and_valid_messages()
    {
        var tok = await f.PairAsync();
        lock (f.Inputs) f.Inputs.Clear();
        using (var anon = await Ws())
        {
            await Send(anon, "{\"t\":\"click\",\"b\":\"left\"}");
            var buf = new byte[64];
            var res = await anon.ReceiveAsync(buf, CancellationToken.None);
            Assert.Equal(WebSocketMessageType.Close, res.MessageType);
        }
        using var ws = await Ws();
        await Send(ws, JsonSerializer.Serialize(new { auth = tok }));
        await Send(ws, "{\"t\":\"move\",\"dx\":5,\"dy\":6}");
        await Send(ws, "{\"t\":\"key\",\"k\":\"win\"}");   // rejected
        await Send(ws, "{\"t\":\"click\",\"b\":\"left\"}");
        for (var i = 0; i < 50; i++) { lock (f.Inputs) if (f.Inputs.Count >= 2) break; await Task.Delay(50); }
        lock (f.Inputs)
        {
            Assert.Equal(2, f.Inputs.Count);
            Assert.Equal(InputKind.Move, f.Inputs[0].Kind);
            Assert.Equal(InputKind.Click, f.Inputs[1].Kind);
        }
    }

    [Fact]
    public async Task Revoked_token_stops_input()
    {
        var tok = await f.PairAsync();
        lock (f.Inputs) f.Inputs.Clear();
        using var ws = await Ws();
        await Send(ws, JsonSerializer.Serialize(new { auth = tok }));
        await Send(ws, "{\"t\":\"click\",\"b\":\"left\"}");
        for (var i = 0; i < 50; i++) { lock (f.Inputs) if (f.Inputs.Count >= 1) break; await Task.Delay(50); }
        f.Pairing.RevokeAll();
        await Send(ws, "{\"t\":\"click\",\"b\":\"right\"}");
        await Task.Delay(300);
        lock (f.Inputs) Assert.Single(f.Inputs);
    }

    [Fact]
    public async Task Server_refuses_to_start_without_certificate2()
    {
        var s = new LinkServer(new PairingService(), null!, null!, Path.Combine(f.Dir, "i3"), Path.Combine(f.Dir, "o3"));
        await Assert.ThrowsAsync<ArgumentNullException>(() => s.StartAsync(null!, 1234));
    }
}
