using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;
using Talkie.Shared;

namespace Talkie.Web.Client.Services;

/// <summary>
/// The logged-in user, their wallet and the public app settings. Also the app's <see cref="AuthenticationStateProvider"/>.
/// </summary>
public sealed class SessionState : AuthenticationStateProvider
{
    private const string StorageKey = "talkie.token";
    private readonly ApiClient _api;
    private readonly TokenStore _tokens;
    private readonly IJSRuntime _js;
    private Task? _init;

    public SessionState(ApiClient api, TokenStore tokens, IJSRuntime js)
    {
        _api = api;
        _tokens = tokens;
        _js = js;
        _api.Unauthorized += async () => await LogoutAsync();
    }

    public UserProfileDto? Me { get; private set; }
    public StatsDto? Stats { get; private set; }
    public PublicSettingsDto Settings { get; private set; } = new("Talkie", null, true);
    public bool IsLoggedIn => Me is not null;

    public event Action? Changed;

    public Task InitializeAsync() => _init ??= InitializeCoreAsync();

    private async Task InitializeCoreAsync()
    {
        try { Settings = await _api.GetAsync<PublicSettingsDto>("/api/auth/settings"); }
        catch (ApiException) { /* keep defaults */ }

        var token = await _js.InvokeAsync<string?>("talkie.storage.get", StorageKey);
        if (string.IsNullOrEmpty(token)) return;

        _tokens.Token = token;
        try
        {
            Me = await _api.GetAsync<UserProfileDto>("/api/account/me");
            await RefreshStatsAsync();
        }
        catch (ApiException)
        {
            _tokens.Token = null;
            Me = null;
            await _js.InvokeVoidAsync("talkie.storage.remove", StorageKey);
        }
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await InitializeAsync();
        if (Me is null) return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Me.Id.ToString()),
            new(ClaimTypes.Name, Me.DisplayName),
            new(ClaimTypes.Email, Me.Email),
        };
        if (Me.IsAdmin) claims.Add(new Claim(ClaimTypes.Role, "admin"));
        if (Me.IsOwner) claims.Add(new Claim(ClaimTypes.Role, "owner"));
        return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity(claims, "talkie")));
    }

    public async Task LoginAsync(AuthResponse response)
    {
        _tokens.Token = response.Token;
        Me = response.User;
        await _js.InvokeVoidAsync("talkie.storage.set", StorageKey, response.Token);
        await RefreshStatsAsync();
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        Changed?.Invoke();
    }

    public async Task LogoutAsync()
    {
        if (Me is null && _tokens.Token is null) return;
        _tokens.Token = null;
        Me = null;
        Stats = null;
        await _js.InvokeVoidAsync("talkie.storage.remove", StorageKey);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        Changed?.Invoke();
    }

    public void SetProfile(UserProfileDto profile)
    {
        Me = profile;
        Changed?.Invoke();
    }

    public async Task RefreshProfileAsync()
    {
        Me = await _api.GetAsync<UserProfileDto>("/api/account/me");
        Changed?.Invoke();
    }

    public async Task RefreshStatsAsync()
    {
        if (_tokens.Token is null) return;
        try
        {
            Stats = await _api.GetAsync<StatsDto>("/api/stats");
            Changed?.Invoke();
        }
        catch (ApiException) { }
    }

    public async Task RefreshSettingsAsync()
    {
        Settings = await _api.GetAsync<PublicSettingsDto>("/api/auth/settings");
        Changed?.Invoke();
    }

    /// <summary>Update the wallet numbers shown in the top bar after a shop action.</summary>
    public void ApplyWallet(WalletDto wallet)
    {
        if (Stats is not null)
        {
            Stats = Stats with { Money = wallet.Money, Shards = wallet.Shards, ShardMultiplier = wallet.ShardMultiplier };
        }
        if (Me is not null && Me.ActiveEffect != wallet.ActiveEffect)
        {
            Me = Me with { ActiveEffect = wallet.ActiveEffect };
        }
        Changed?.Invoke();
    }
}
