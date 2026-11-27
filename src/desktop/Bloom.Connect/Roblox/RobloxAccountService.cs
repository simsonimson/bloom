using Bloom.Core.Connect;
using Bloom.Core.Events;

namespace Bloom.Connect.Roblox;

/// <summary>
/// Links a Roblox account through the official OAuth flow (C4) and keeps the session alive (C5).
/// bloom never sees the password: sign-in happens on roblox.com in the browser.
/// </summary>
public sealed class RobloxAccountService
{
    private static readonly TimeSpan PendingLifetime = TimeSpan.FromMinutes(10);

    private readonly RobloxOAuth _oauth;
    private readonly SessionVault _vault;
    private readonly Func<string?> _clientId;
    private readonly IEventBus _events;
    private readonly Action<string, Exception?> _log;
    private readonly Action<string> _openBrowser;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (Pkce Pkce, string State, DateTimeOffset Created)? _pending;

    public RobloxAccountService(RobloxOAuth oauth, SessionVault vault, Func<string?> clientId, IEventBus events,
        Action<string, Exception?> log, Action<string> openBrowser)
    {
        _oauth = oauth;
        _vault = vault;
        _clientId = clientId;
        _events = events;
        _log = log;
        _openBrowser = openBrowser;
    }

    public RobloxAccountState State { get; private set; } = RobloxAccountState.SignedOut;
    public RobloxProfile? Profile { get; private set; }

    /// <summary>True while the profile shown is the cached one because Roblox was unreachable.</summary>
    public bool Offline { get; private set; }

    /// <summary>Startup: refresh the stored session and the profile.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_clientId()))
        {
            Set(RobloxAccountState.NotConfigured, null, false);
            return;
        }
        var stored = _vault.Load();
        if (stored is null)
        {
            Set(RobloxAccountState.SignedOut, null, false);
            return;
        }
        Set(RobloxAccountState.SignedIn, stored.Profile, offline: true);
        await RefreshAsync(ct);
    }

    /// <summary>Rotates the refresh token and reloads the profile.</summary>
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var clientId = _clientId();
            var stored = _vault.Load();
            if (string.IsNullOrWhiteSpace(clientId) || stored is null) return;
            try
            {
                var tokens = await _oauth.RefreshAsync(clientId, stored.RefreshToken, ct);
                // Save the rotated token before anything else can fail.
                _vault.Save(stored with { RefreshToken = tokens.RefreshToken, SavedAt = DateTimeOffset.UtcNow });
                var profile = await _oauth.GetProfileAsync(tokens.AccessToken, ct);
                _vault.Save(new StoredSession(tokens.RefreshToken, profile, DateTimeOffset.UtcNow));
                Set(RobloxAccountState.SignedIn, profile, false);
            }
            catch (OAuthGrantException ex)
            {
                _log($"Roblox session rejected ({ex.Message})", null);
                _vault.Delete();
                Set(RobloxAccountState.Expired, stored.Profile, false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                _log("Roblox unreachable, keeping cached profile", ex);
                Set(RobloxAccountState.SignedIn, stored.Profile, true);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Opens the Roblox sign-in page. False if no client ID is configured.</summary>
    public bool BeginSignIn()
    {
        var clientId = _clientId();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            Set(RobloxAccountState.NotConfigured, null, false);
            return false;
        }
        var pkce = Pkce.Create();
        var state = Pkce.RandomState();
        _pending = (pkce, state, DateTimeOffset.UtcNow);
        Set(RobloxAccountState.SigningIn, Profile, false);
        _openBrowser(RobloxOAuth.AuthorizeUrl(clientId, pkce, state));
        return true;
    }

    public void CancelSignIn()
    {
        _pending = null;
        if (State == RobloxAccountState.SigningIn) Set(_vault.Load() is null ? RobloxAccountState.SignedOut : RobloxAccountState.SignedIn, Profile, Offline);
    }

    /// <summary>Handles bloom://oauth/callback. Returns a result for the UI.</summary>
    public async Task<CallbackResult> CompleteSignInAsync(string callbackUri, CancellationToken ct = default)
    {
        var pending = _pending;
        var clientId = _clientId();
        if (pending is null || DateTimeOffset.UtcNow - pending.Value.Created > PendingLifetime || string.IsNullOrWhiteSpace(clientId))
        {
            _pending = null;
            return CallbackResult.StateMismatch;
        }

        var result = RobloxOAuth.ParseCallback(callbackUri, pending.Value.State, out var code);
        if (result != CallbackResult.Ok)
        {
            if (result == CallbackResult.Denied) CancelSignIn();
            return result;
        }
        _pending = null;

        await _gate.WaitAsync(ct);
        try
        {
            var tokens = await _oauth.ExchangeCodeAsync(clientId, code!, pending.Value.Pkce.Verifier, ct);
            _vault.Save(new StoredSession(tokens.RefreshToken, null, DateTimeOffset.UtcNow));
            var profile = await _oauth.GetProfileAsync(tokens.AccessToken, ct);
            _vault.Save(new StoredSession(tokens.RefreshToken, profile, DateTimeOffset.UtcNow));
            Set(RobloxAccountState.SignedIn, profile, false);
            return CallbackResult.Ok;
        }
        catch (Exception ex)
        {
            _log("Roblox sign-in failed", ex);
            Set(_vault.Load() is null ? RobloxAccountState.SignedOut : RobloxAccountState.SignedIn, Profile, Offline);
            return CallbackResult.Invalid;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        var stored = _vault.Load();
        _vault.Delete();
        Set(RobloxAccountState.SignedOut, null, false);
        if (stored is not null && _clientId() is { Length: > 0 } clientId)
        {
            try { await _oauth.RevokeAsync(clientId, stored.RefreshToken, ct); }
            catch (Exception ex) { _log("Revoking the Roblox token failed", ex); }
        }
    }

    private void Set(RobloxAccountState state, RobloxProfile? profile, bool offline)
    {
        State = state;
        Profile = profile;
        Offline = offline;
        _events.Publish(new RobloxAccountChanged(state, profile, offline));
    }
}
