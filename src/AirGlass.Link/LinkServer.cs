using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using AirGlass.Core.Link;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AirGlass.Link;

/// <summary>
/// HTTPS-only server for the phone page. There is deliberately no plain-HTTP mode: the
/// caller must supply a certificate the phone's browser already trusts.
/// </summary>
public sealed class LinkServer : IAsyncDisposable
{
    public const long MaxUploadBytes = 200L * 1024 * 1024;
    private readonly PairingService _pairing;
    private readonly IInputSink _input;
    private readonly IClipboardSink _clip;
    private readonly string _inbox;
    private readonly string _outbox;
    private WebApplication? _app;
    private int _activeSockets;

    public bool InputEnabled { get; set; } = true;
    public event Action<PendingDevice>? PairRequested;
    public event Action<string>? Log;

    public LinkServer(PairingService pairing, IInputSink input, IClipboardSink clip, string inbox, string outbox)
    {
        _pairing = pairing; _input = input; _clip = clip; _inbox = inbox; _outbox = outbox;
        Directory.CreateDirectory(inbox);
        Directory.CreateDirectory(outbox);
    }

    public int Port { get; private set; }

    public Task StartAsync(X509Certificate2 cert, int port) => StartAsync(cert, port, null);

    public async Task StartAsync(X509Certificate2 cert, int port, IPAddress? bindTo)
    {
        if (cert is null) throw new ArgumentNullException(nameof(cert), "HTTPS certificate required; plain HTTP is not supported.");
        if (!cert.HasPrivateKey) throw new ArgumentException("Certificate has no private key.", nameof(cert));
        var b = WebApplication.CreateSlimBuilder();
        b.Logging.ClearProviders();
        b.WebHost.ConfigureKestrel(k =>
        {
            k.AddServerHeader = false;
            k.Limits.MaxRequestBodySize = MaxUploadBytes;
            k.Listen(bindTo ?? IPAddress.Any, port, o =>
            {
                o.Protocols = HttpProtocols.Http1;
                o.UseHttps(cert);
            });
        });
        var app = b.Build();
        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
        app.Use(async (ctx, next) =>
        {
            ctx.Response.Headers["Cache-Control"] = "no-store";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
            ctx.Response.Headers["X-Frame-Options"] = "DENY";
            ctx.Response.Headers["Content-Security-Policy"] =
                "default-src 'none'; script-src 'self' 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
            await next();
        });
        Map(app);
        await app.StartAsync();
        Port = new Uri(app.Urls.FirstOrDefault() ?? $"https://0.0.0.0:{port}").Port;
        if (Port == 0) Port = port;
        _app = app;
        Log?.Invoke($"Link listening on https port {Port}");
    }

    private static bool SameOrigin(HttpContext c)
    {
        // Pages we serve only talk to us. Reject cross-site requests that carry an Origin header.
        if (!c.Request.Headers.TryGetValue("Origin", out var o) || o.Count == 0) return true;
        return Uri.TryCreate(o[0], UriKind.Absolute, out var u) && string.Equals(u.Authority, c.Request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    private string? Auth(HttpContext c)
    {
        if (!SameOrigin(c)) return null;
        var h = c.Request.Headers.Authorization.ToString();
        return h.StartsWith("Bearer ", StringComparison.Ordinal) ? _pairing.Validate(h[7..]) : null;
    }

    private void Map(WebApplication app)
    {
        app.MapGet("/", async ctx =>
        {
            using var s = typeof(LinkServer).Assembly.GetManifestResourceStream("phone.html")!;
            ctx.Response.ContentType = "text/html; charset=utf-8";
            await s.CopyToAsync(ctx.Response.Body);
        });

        app.MapPost("/api/pair", async ctx =>
        {
            if (!SameOrigin(ctx)) { ctx.Response.StatusCode = 403; return; }
            PairBody? body = null;
            try { body = await JsonSerializer.DeserializeAsync<PairBody>(ctx.Request.Body, JsonOpts); } catch { }
            if (body is null) { ctx.Response.StatusCode = 400; return; }
            var r = _pairing.Submit(body.Code ?? "", body.Name ?? "", out var dev);
            if (r == PairResult.Pending)
            {
                PairRequested?.Invoke(dev!);
                await ctx.Response.WriteAsJsonAsync(new { id = dev!.Id });
                return;
            }
            ctx.Response.StatusCode = r == PairResult.LockedOut ? 429 : 400;
            await ctx.Response.WriteAsJsonAsync(new { error = r.ToString() });
        });

        app.MapGet("/api/pair/{id}", async ctx =>
        {
            if (!SameOrigin(ctx)) { ctx.Response.StatusCode = 403; return; }
            var (st, tok) = _pairing.Poll((string)ctx.Request.RouteValues["id"]!);
            await ctx.Response.WriteAsJsonAsync(new { status = st, token = tok });
        });

        app.MapGet("/api/me", async ctx =>
        {
            var who = Auth(ctx);
            if (who is null) { ctx.Response.StatusCode = 401; return; }
            await ctx.Response.WriteAsJsonAsync(new { device = who, input = InputEnabled });
        });

        app.MapPost("/api/files", async ctx =>
        {
            if (Auth(ctx) is null) { ctx.Response.StatusCode = 401; return; }
            var raw = Uri.UnescapeDataString(ctx.Request.Headers["X-Filename"].ToString());
            if (ctx.Request.ContentLength is > MaxUploadBytes) { ctx.Response.StatusCode = 413; return; }
            var path = SafeFiles.UniquePath(_inbox, raw);
            long n = 0;
            try
            {
                await using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var buf = new byte[81920];
                int r;
                while ((r = await ctx.Request.Body.ReadAsync(buf)) > 0)
                {
                    n += r;
                    if (n > MaxUploadBytes) throw new IOException("too large");
                    await fs.WriteAsync(buf.AsMemory(0, r));
                }
            }
            catch
            {
                try { File.Delete(path); } catch { }
                ctx.Response.StatusCode = 413;
                return;
            }
            Log?.Invoke($"Received file {Path.GetFileName(path)} ({n} bytes)");
            await ctx.Response.WriteAsJsonAsync(new { name = Path.GetFileName(path), bytes = n });
        });

        app.MapGet("/api/files", async ctx =>
        {
            if (Auth(ctx) is null) { ctx.Response.StatusCode = 401; return; }
            var list = new DirectoryInfo(_outbox).GetFiles()
                .Where(f => (f.Attributes & FileAttributes.ReparsePoint) == 0 && (f.Attributes & FileAttributes.Hidden) == 0)
                .OrderByDescending(f => f.LastWriteTimeUtc).Take(200)
                .Select(f => new { name = f.Name, bytes = f.Length });
            await ctx.Response.WriteAsJsonAsync(list);
        });

        app.MapGet("/api/files/{name}", async ctx =>
        {
            if (Auth(ctx) is null) { ctx.Response.StatusCode = 401; return; }
            var path = SafeFiles.ResolveShared(_outbox, (string)ctx.Request.RouteValues["name"]!);
            if (path is null) { ctx.Response.StatusCode = 404; return; }
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.Headers.ContentDisposition = "attachment; filename*=UTF-8''" + Uri.EscapeDataString(Path.GetFileName(path));
            await ctx.Response.SendFileAsync(path);
        });

        app.MapPost("/api/clip", async ctx =>
        {
            if (Auth(ctx) is null) { ctx.Response.StatusCode = 401; return; }
            using var sr = new StreamReader(ctx.Request.Body, Encoding.UTF8);
            var buf = new char[InputParser.MaxText * 20 + 1];
            var n = await sr.ReadBlockAsync(buf, 0, buf.Length);
            if (n > InputParser.MaxText * 20) { ctx.Response.StatusCode = 413; return; }
            _clip.SetText(new string(buf, 0, n));
            ctx.Response.StatusCode = 204;
        });

        app.MapGet("/api/clip", async ctx =>
        {
            if (Auth(ctx) is null) { ctx.Response.StatusCode = 401; return; }
            ctx.Response.ContentType = "text/plain; charset=utf-8";
            await ctx.Response.WriteAsync(_clip.GetText() ?? "");
        });

        app.Map("/ws", async ctx =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest || !SameOrigin(ctx)) { ctx.Response.StatusCode = 400; return; }
            // Browsers cannot set headers on WebSockets, so the token is the first message, never in the URL.
            if (Interlocked.Increment(ref _activeSockets) > 4) { Interlocked.Decrement(ref _activeSockets); ctx.Response.StatusCode = 503; return; }
            try
            {
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                var buf = new byte[InputParser.MaxMessageBytes];
                string? who = null; string? sockTok = null;
                var authDeadline = DateTime.UtcNow.AddSeconds(10);
                while (ws.State == WebSocketState.Open)
                {
                    using var cts = new CancellationTokenSource(who is null ? TimeSpan.FromSeconds(10) : TimeSpan.FromMinutes(30));
                    var res = await ws.ReceiveAsync(buf, cts.Token);
                    if (res.MessageType == WebSocketMessageType.Close) break;
                    if (!res.EndOfMessage || res.MessageType != WebSocketMessageType.Text)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.MessageTooBig, "bad", CancellationToken.None);
                        break;
                    }
                    var text = Encoding.UTF8.GetString(buf, 0, res.Count);
                    if (who is null)
                    {
                        string? tok = null;
                        try { tok = JsonDocument.Parse(text).RootElement.GetProperty("auth").GetString(); } catch { }
                        who = _pairing.Validate(tok); sockTok = tok;
                        if (who is null || DateTime.UtcNow > authDeadline)
                        {
                            await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "auth", CancellationToken.None);
                            break;
                        }
                        continue;
                    }
                    // Token may have been revoked since connect.
                    if (_pairing.Validate(sockTok) is null)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "revoked", CancellationToken.None);
                        break;
                    }
                    if (!InputEnabled) continue;
                    if (InputParser.TryParse(text, out var msg)) _input.Apply(msg!);
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
            finally { Interlocked.Decrement(ref _activeSockets); }
        });
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };
    private sealed record PairBody(string? Code, string? Name);

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); _app = null; }
    }
}
