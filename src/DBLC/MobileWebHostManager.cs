using System.Net;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Windows.Threading;

namespace DBLC;

public sealed class MobileUiDispatcher(Dispatcher dispatcher)
{
    public Task InvokeAsync(Action action) => dispatcher.InvokeAsync(action).Task;
    public Task InvokeAsync(Func<Task> action) => dispatcher.InvokeAsync(action).Task.Unwrap();
    public Task<T> InvokeAsync<T>(Func<T> action) => dispatcher.InvokeAsync(action).Task;
}

/// <summary>Hosts the browser UI inside the desktop process and shares its authoritative singleton state.</summary>
public sealed class MobileWebHostManager : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AppState? _state;
    private IServiceProvider? _desktopServices;
    private Dispatcher? _dispatcher;
    private WebApplication? _web;
    private bool? _configuredEnabled;
    private int _configuredPort;
    private string? _configuredAccessCodeHash;
    private bool _disposed;

    public string Status { get; private set; } = "Mobile access is off.";
    public string LocalAddress => _state is null ? "" : $"http://127.0.0.1:{_state.MobileRemoteAccessPort}";
    public event Action? Changed;

    public void Initialize(IServiceProvider desktopServices, Dispatcher dispatcher)
    {
        if (_state is not null) return;
        _desktopServices = desktopServices;
        _dispatcher = dispatcher;
        _state = desktopServices.GetRequiredService<AppState>();
        _state.Changed += OnStateChanged;
        _ = SynchronizeAsync();
    }

    private void OnStateChanged() => _ = SynchronizeAsync();

    private async Task SynchronizeAsync()
    {
        if (_state is null || _desktopServices is null || _dispatcher is null || _disposed) return;
        if (_configuredEnabled == _state.MobileRemoteAccessEnabled && _configuredPort == _state.MobileRemoteAccessPort && _configuredAccessCodeHash == _state.MobileAccessCodeHash) return;
        await _gate.WaitAsync();
        try
        {
            var enabled = _state.MobileRemoteAccessEnabled;
            var port = _state.MobileRemoteAccessPort;
            var accessCodeHash = _state.MobileAccessCodeHash;
            if (_configuredEnabled == enabled && _configuredPort == port && _configuredAccessCodeHash == accessCodeHash) return;
            if (_web is not null)
            {
                new GmRequestDiagnostics().RecordMobileHostEvent("stopping_for_configuration_change");
                await _web.StopAsync(TimeSpan.FromSeconds(2));
                await _web.DisposeAsync();
                _web = null;
                new GmRequestDiagnostics().RecordMobileHostEvent("stopped");
            }
            _configuredEnabled = enabled;
            _configuredPort = port;
            _configuredAccessCodeHash = accessCodeHash;
            if (!enabled)
            {
                Status = "Mobile access is off.";
                Changed?.Invoke();
                return;
            }
            try
            {
                _web = BuildWebHost(_desktopServices, _dispatcher, _state, port);
                await _web.StartAsync();
                new GmRequestDiagnostics().RecordMobileHostEvent("started");
                Status = $"Ready on this PC at {LocalAddress}. Use Tailscale Serve to reach it privately.";
            }
            catch (Exception ex)
            {
                new GmRequestDiagnostics().RecordMobileHostEvent("start_failed");
                if (_web is not null) { await _web.DisposeAsync(); _web = null; }
                _configuredEnabled = enabled;
                Status = $"Mobile host could not start: {ex.GetType().Name}. Check the port and try again.";
            }
            Changed?.Invoke();
        }
        finally { _gate.Release(); }
    }

    private static WebApplication BuildWebHost(IServiceProvider desktop, Dispatcher dispatcher, AppState state, int port)
    {
        var appDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var webRoot = Path.Combine(appDirectory, "wwwroot");
        PhysicalFileProvider? webRootFileProvider = null;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(MobileHostDocument).Assembly.GetName().Name,
            ContentRootPath = appDirectory,
            WebRootPath = webRoot,
            Args = []
        });
        // Self-extracting single-file apps report the extraction directory as
        // AppContext.BaseDirectory. Browser assets ship beside DBLC.exe, so pin
        // the host to that directory for CSS, JS and images.
        if (Directory.Exists(webRoot))
        {
            webRootFileProvider = new PhysicalFileProvider(webRoot);
            builder.Environment.WebRootFileProvider = webRootFileProvider;
        }
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, port));
        builder.Logging.AddProvider(new MobileCircuitErrorLogger());
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddScoped<CircuitHandler, MobileCircuitDiagnostics>();
        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "DBLC.Mobile.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.LoginPath = "/login";
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
            options.Events.OnValidatePrincipal = context =>
            {
                if (!state.MobileRemoteAccessEnabled || context.Principal?.FindFirstValue("mobile_code_hash") != state.MobileAccessCodeHash)
                    context.RejectPrincipal();
                return Task.CompletedTask;
            };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery(options => options.Cookie.Name = "DBLC.Mobile.AntiForgery");
        var keyDirectory = Environment.GetEnvironmentVariable("DBLC_MOBILE_KEY_DIRECTORY");
        if (string.IsNullOrWhiteSpace(keyDirectory))
            keyDirectory = AppDataPaths.File("MobileKeysProtected");
        Directory.CreateDirectory(keyDirectory);
        builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory)).ProtectKeysWithDpapi().SetApplicationName("DBLC.Mobile");
        builder.Services.AddSingleton(state);
        builder.Services.AddSingleton(desktop.GetRequiredService<LlamaClient>());
        builder.Services.AddSingleton(desktop.GetRequiredService<PaperDocumentState>());
        // AppShell is shared with the desktop interface. Register its existing
        // desktop singletons in this host too so opening a mobile space does not
        // fail component activation and terminate the Blazor circuit.
        builder.Services.AddSingleton(desktop.GetRequiredService<PaperWindowManager>());
        builder.Services.AddSingleton(desktop.GetRequiredService<CampaignWindowManager>());
        builder.Services.AddSingleton(desktop.GetRequiredService<MobileWebHostManager>());
        builder.Services.AddSingleton(desktop.GetRequiredService<PlayerImageAssetStore>());
        builder.Services.AddSingleton(desktop.GetRequiredService<ViewOrderStore>());
        builder.Services.AddSingleton(desktop.GetRequiredService<PrivacyLockStore>());
        builder.Services.AddSingleton(desktop.GetRequiredService<LlamaModelInfoClient>());
        builder.Services.AddSingleton(desktop.GetRequiredService<NpcPortraitStore>());
        builder.Services.AddSingleton(desktop.GetRequiredService<CampaignMapAssetStore>());
        builder.Services.AddSingleton(desktop.GetRequiredService<GmActivityCatalog>());
        builder.Services.AddSingleton(desktop.GetRequiredService<ChronicleImageAssetStore>());
        builder.Services.AddSingleton(new MobileUiDispatcher(dispatcher));

        var app = builder.Build();
        if (webRootFileProvider is not null)
            app.Lifetime.ApplicationStopped.Register(webRootFileProvider.Dispose);
        var forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost, ForwardLimit = 1 };
        forwarded.KnownProxies.Add(IPAddress.Loopback);
        forwarded.KnownProxies.Add(IPAddress.IPv6Loopback);
        app.UseForwardedHeaders(forwarded);
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/login") { await next(); return; }
            var auth = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (!auth.Succeeded)
            {
                context.Response.Redirect("/login");
                return;
            }
            context.User = auth.Principal!;
            if (IsBrowserMutation(context.Request) && !IsSameOrigin(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next();
        });
        app.UseAntiforgery();
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/_blazor")) { await next(); return; }
            var timer = Stopwatch.StartNew();
            var webSocket = context.WebSockets.IsWebSocketRequest;
            string? error = null;
            try { await next(); }
            catch (Exception ex) { error = ex.GetType().FullName; throw; }
            finally
            {
                new GmRequestDiagnostics().RecordMobileTransport(context.Request.Method,
                    context.Response.StatusCode, webSocket, timer.Elapsed, error);
            }
        });

        app.MapGet("/login", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var token = antiforgery.GetAndStoreTokens(context).RequestToken ?? "";
            return Results.Content(LoginHtml(token, ""), "text/html; charset=utf-8");
        });
        app.MapPost("/login", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.Content(LoginHtml("", "Reload this page and try again."), "text/html; charset=utf-8", statusCode: 400); }
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (!state.VerifyMobileAccessCode(form["code"].ToString()))
                return Results.Content(LoginHtml("", "That access code was not accepted."), "text/html; charset=utf-8", statusCode: 401);
            var identity = new ClaimsIdentity([
                new Claim(ClaimTypes.Name, "DBLC player"),
                new Claim("mobile_code_hash", state.MobileAccessCodeHash ?? "")
            ], CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
            return Results.Redirect("/");
        }).DisableAntiforgery();
        app.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery) =>
        {
            try { await antiforgery.ValidateRequestAsync(context); }
            catch (AntiforgeryValidationException) { return Results.BadRequest(); }
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/login");
        }).DisableAntiforgery();
        app.MapGet("/library/{bookId}", (string bookId) =>
        {
            var pdfPath = state.GetLibraryPdfPath(bookId);
            return pdfPath is null ? Results.NotFound() : Results.File(pdfPath, "application/pdf", enableRangeProcessing: true);
        });
        app.MapGet("/campaign-maps/{campaignId:guid}/{mapId:guid}/{kind}", (HttpContext context, Guid campaignId, Guid mapId, string kind) =>
        {
            if (kind is not ("original" or "thumbnail")) return Results.NotFound();
            var image = state.GetCampaignMapImage(campaignId, mapId, kind);
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return image is null ? Results.NotFound() : Results.File(image.Value.Path, image.Value.Mime, enableRangeProcessing: true);
        });
        app.MapGet("/chronicle-portraits/{campaignId:guid}/{kind}/{id}", (HttpContext context, Guid campaignId, string kind, string id) =>
        {
            if (!state.Campaigns.Any(campaign => campaign.Id == campaignId)) return Results.NotFound();
            string? path = null;
            if (kind == "player")
                path = desktop.GetRequiredService<PlayerImageAssetStore>().FindImagePath(campaignId, "assets/" + id);
            else if (kind == "npc" && Guid.TryParse(id, out var npcId))
            {
                var npc = state.GetNpcs(campaignId).FirstOrDefault(item => item.Id == npcId);
                if (npc is not null) path = desktop.GetRequiredService<NpcPortraitStore>().FindImagePath(campaignId, npcId, npc.PortraitExtension);
            }
            if (path is null) return Results.NotFound();
            context.Response.Headers.CacheControl = "private, no-cache";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            var mime = Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".webp" => "image/webp", _ => "application/octet-stream"
            };
            return Results.File(path, mime, lastModified: File.GetLastWriteTimeUtc(path));
        });
        app.MapRazorComponents<MobileHostDocument>().AddInteractiveServerRenderMode();
        return app;
    }

    private static bool IsBrowserMutation(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) || HttpMethods.IsPut(request.Method) || HttpMethods.IsPatch(request.Method) || HttpMethods.IsDelete(request.Method) ||
        string.Equals(request.Headers.Upgrade, "websocket", StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrigin(HttpRequest request)
    {
        if (!Uri.TryCreate(request.Headers.Origin.ToString(), UriKind.Absolute, out var origin)) return false;
        return string.Equals(origin.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(origin.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    private static string LoginHtml(string token, string message) => """
        <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>DBLC · Sign in</title>
        <style>body{margin:0;min-height:100vh;display:grid;place-items:center;background:#111;color:#ddd;font:14px 'Segoe UI',sans-serif}.box{width:min(360px,calc(100vw - 44px));padding:25px;background:#1b1b1b;border:1px solid #444;border-radius:8px}h1{font:500 25px Georgia;margin:0 0 8px}.muted{color:#999;line-height:1.5;font-size:12px}label{display:grid;gap:8px;margin-top:20px;color:#aaa;font-size:11px}input{padding:11px;background:#111;border:1px solid #444;border-radius:4px;color:#eee;font:12px monospace}.button{margin-top:14px;width:100%;padding:10px;border:1px solid #888;border-radius:4px;background:#999;color:#151515;font-weight:600}.error{color:#d5a2a2;font-size:12px;margin-top:12px}</style></head><body><main class="box"><h1>DBLC</h1><div class="muted">Private access · enter the one-time code shown in DBLC Settings.</div>
        <form method="post" action="/login"><input type="hidden" name="__RequestVerificationToken" value="__TOKEN__"><label>Mobile access code<input name="code" type="password" autocomplete="current-password" required autofocus></label><button class="button" type="submit">Open DBLC</button></form><div class="error">__MESSAGE__</div></main></body></html>
        """.Replace("__TOKEN__", System.Net.WebUtility.HtmlEncode(token), StringComparison.Ordinal)
            .Replace("__MESSAGE__", System.Net.WebUtility.HtmlEncode(message), StringComparison.Ordinal);

    public async Task StopAsync()
    {
        if (_disposed) return;
        _disposed = true;
        if (_state is not null) _state.Changed -= OnStateChanged;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_web is not null)
            {
                new GmRequestDiagnostics().RecordMobileHostEvent("stopping_for_shutdown");
                await _web.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                await _web.DisposeAsync().ConfigureAwait(false);
                _web = null;
                new GmRequestDiagnostics().RecordMobileHostEvent("stopped");
            }
            Status = "Mobile host stopped.";
        }
        finally { _gate.Release(); }
        Changed?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _gate.Dispose();
    }
}

internal sealed class MobileCircuitDiagnostics : CircuitHandler
{
    private long _disconnectedAt;

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Record("opened", circuit);
        return Task.CompletedTask;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var disconnectedAt = Interlocked.Exchange(ref _disconnectedAt, 0);
        Record(disconnectedAt == 0 ? "connected" : "reconnected", circuit,
            disconnectedAt == 0 ? null : Stopwatch.GetElapsedTime(disconnectedAt).TotalMilliseconds);
        return Task.CompletedTask;
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _disconnectedAt, Stopwatch.GetTimestamp());
        Record("disconnected", circuit);
        return Task.CompletedTask;
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        Record("closed", circuit);
        return Task.CompletedTask;
    }

    private static void Record(string phase, Circuit circuit, double? elapsedMs = null)
    {
        // Hash the circuit ID so events can be correlated without storing its reconnect token.
        var tag = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(circuit.Id)))[..12];
        new GmRequestDiagnostics().RecordMobileConnectionEvent(phase, tag, elapsedMs);
    }
}

internal sealed class MobileCircuitErrorLogger : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new ErrorLogger(categoryName);
    public void Dispose() { }
    private sealed class ErrorLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            if (exception is not null && category.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal))
                new GmRequestDiagnostics().RecordMobileCircuitError(category, eventId.Id, exception);
            else if (category.StartsWith("Microsoft.AspNetCore.Http.Connections", StringComparison.Ordinal) ||
                     category.StartsWith("Microsoft.AspNetCore.SignalR", StringComparison.Ordinal))
                new GmRequestDiagnostics().RecordMobileTransportWarning(category, eventId.Id, exception);
        }
    }
}
