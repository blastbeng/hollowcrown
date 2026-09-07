using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
using Hollowcrown.Networking;
using Hollowcrown.Shared;
using HttpClient = System.Net.Http.HttpClient;

namespace Hollowcrown.UI;

/// <summary>
/// REST client for the central server (vision Section 4): auth token persisted
/// to user://, characters CRUD, server list. All calls are async Task — never
/// async void. Default base URL is localhost:6560; override with HC_CENTRAL_URL.
/// </summary>
public partial class CentralClient : Node
{
    public const string DefaultBaseUrl = "http://localhost:6560";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string TokenPath = "user://central_auth.json";

    [Signal] public delegate void LoggedInEventHandler(string username);
    [Signal] public delegate void AuthFailedEventHandler(string message);

    public string Token { get; private set; } = "";
    public string Username { get; private set; } = "";
    public bool IsAuthenticated => Token.Length > 0;

    /// <summary>Match-server token minted by /servers/register (Vision 4).
    /// Sent as the Authorization bearer for heartbeats and as X-Server-Token
    /// on progression saves relayed by an authenticated client.</summary>
    public string ServerToken { get; set; } = "";

    /// <summary>Base URL override (DedicatedServer --central; env still wins
    /// for playtester runs that set HC_CENTRAL_URL).</summary>
    public string BaseUrl { get; set; } = "";

    private string EffectiveBaseUrl =>
        OS.GetEnvironment("HC_CENTRAL_URL") is { Length: > 0 } env ? env
        : BaseUrl.Length > 0 ? BaseUrl : DefaultBaseUrl;

    /// <summary>Env-resolved base URL for static callers (CombatAuthority's
    /// MMR reporter runs outside any bound client instance).</summary>
    public static string EnvBaseUrl() =>
        OS.GetEnvironment("HC_CENTRAL_URL") is { Length: > 0 } env ? env : DefaultBaseUrl;

    public override void _Ready() => LoadToken();

    public async Task Register(string user, string pass) => await Auth("auth/register", user, pass);
    public async Task Login(string user, string pass) => await Auth("auth/login", user, pass);

    private async Task Auth(string path, string user, string pass)
    {
        try
        {
            using var resp = await Http.PostAsJsonAsync($"{EffectiveBaseUrl}/{path}", new AuthRequest(user, pass), JsonOpts);
            if (!resp.IsSuccessStatusCode)
            {
                EmitSignal(SignalName.AuthFailed, await ErrorText(resp, "rejected by central server"));
                return;
            }
            var body = await resp.Content.ReadFromJsonAsync<AuthResponse>(JsonOpts);
            Token = body!.Token;
            Username = body.Username;
            SaveToken();
            EmitSignal(SignalName.LoggedIn, Username);
        }
        catch (Exception e)
        {
            EmitSignal(SignalName.AuthFailed, $"central unreachable: {e.Message}");
        }
    }

    public async Task<List<CharacterDto>?> ListCharacters()
    {
        try
        {
            using var resp = await Authed(HttpMethod.Get, "characters");
            return resp.IsSuccessStatusCode
                ? await resp.Content.ReadFromJsonAsync<List<CharacterDto>>(JsonOpts)
                : null;
        }
        catch (Exception)
        {
            return null;   // caller shows "central unreachable?" status
        }
    }

    public async Task<CharacterDto?> CreateCharacter(string name, string classId)
    {
        try
        {
            using var resp = await Authed(HttpMethod.Post, "characters",
                new CreateCharacterRequest(name, classId));
            return resp.IsSuccessStatusCode
                ? await resp.Content.ReadFromJsonAsync<CharacterDto>(JsonOpts)
                : null;
        }
        catch (Exception)
        {
            return null;   // caller shows "central unreachable?" status
        }
    }

    public async Task<CharacterDto?> SaveProgress(int characterId, int level, int xp, string gearJson)
    {
        try
        {
            // Vision 4: the PUT requires BOTH identities — the USER token as the
            // bearer (ownership) and the realm's match-server token (received at
            // spawn approval) as X-Server-Token. The client never invents it.
            using var req = new HttpRequestMessage(HttpMethod.Put, $"{EffectiveBaseUrl}/characters/{characterId}/progress")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new ProgressRequest(level, xp, gearJson), JsonOpts),
                    System.Text.Encoding.UTF8, "application/json"),
            };
            if (Token.Length > 0)
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            if (CombatAuthority.ServerToken.Length > 0)
                req.Headers.Add("X-Server-Token", CombatAuthority.ServerToken);
            using var resp = await Http.SendAsync(req);
            return resp.IsSuccessStatusCode
                ? await resp.Content.ReadFromJsonAsync<CharacterDto>(JsonOpts)
                : null;
        }
        catch (Exception)
        {
            return null;   // caller decides how to surface the failure
        }
    }

    /// <summary>Match-server registration (Vision 4): mints the server token
    /// used for heartbeats, progression saves and MMR reports.</summary>
    public async Task<ServerTokenResponse?> RegisterServer(ServerRegistration reg)
    {
        try
        {
            using var resp = await Http.PostAsJsonAsync($"{EffectiveBaseUrl}/servers/register", reg, JsonOpts);
            return resp.IsSuccessStatusCode
                ? await resp.Content.ReadFromJsonAsync<ServerTokenResponse>(JsonOpts)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Elo report (Vision 8): the MATCH SERVER reports the duel
    /// result; central owns the rating. Returns the applied result.</summary>
    public async Task<MmrResult?> ReportMmr(MmrReport report)
    {
        try
        {
            using var resp = await Http.PostAsJsonAsync($"{EffectiveBaseUrl}/mmr/report", report, JsonOpts);
            return resp.IsSuccessStatusCode
                ? await resp.Content.ReadFromJsonAsync<MmrResult>(JsonOpts)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Leaderboard top 50 (Vision 8: visible in client + results).</summary>
    public async Task<List<LeaderboardEntry>?> ListLeaderboard()
    {
        try
        {
            using var resp = await Http.GetAsync($"{EffectiveBaseUrl}/leaderboard");
            return resp.IsSuccessStatusCode
                ? await resp.Content.ReadFromJsonAsync<List<LeaderboardEntry>>(JsonOpts)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Character the user picked on the character-select screen
    /// (Vision 6.10): the progression session + class flow read this.</summary>
    public CharacterDto? SelectedCharacter { get; set; }

    public async Task<List<ServerInfo>?> ListServers(string mode = "")
    {
        var query = mode.Length > 0 ? $"?mode={Uri.EscapeDataString(mode)}" : "";
        try
        {
            using var resp = await Authed(HttpMethod.Get, $"servers{query}");
            return resp.IsSuccessStatusCode
                ? await resp.Content.ReadFromJsonAsync<List<ServerInfo>>(JsonOpts)
                : null;
        }
        catch (Exception)
        {
            return null;   // caller shows "central unreachable?" status
        }
    }

    /// <summary>Match-server registry heartbeat — carries the server token
    /// minted at /servers/register (Vision 4).</summary>
    public async Task<bool> Heartbeat(ServerRegistration reg)
    {
        try
        {
            using var msg = new HttpRequestMessage(HttpMethod.Post, $"{EffectiveBaseUrl}/servers/heartbeat")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(reg, JsonOpts),
                    System.Text.Encoding.UTF8, "application/json"),
            };
            if (ServerToken.Length > 0)
                msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ServerToken);
            using var resp = await Http.SendAsync(msg);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Authed request. The user token goes on everything; match-server
    /// routes additionally carry the SERVER token (Vision 4 authority split:
    /// user token identifies the player, server token identifies the realm).</summary>
    private async Task<HttpResponseMessage> Authed(HttpMethod method, string path,
        object? body = null, string? bearer = null)
    {
        var req = new HttpRequestMessage(method, $"{EffectiveBaseUrl}/{path}");
        var token = bearer ?? Token;
        if (token.Length > 0)
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            req.Content = new StringContent(
                JsonSerializer.Serialize(body, body.GetType(), JsonOpts),
                System.Text.Encoding.UTF8, "application/json");
        }
        return await Http.SendAsync(req);
    }

    private static async Task<string> ErrorText(HttpResponseMessage resp, string fallback)
    {
        try
        {
            var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>(JsonOpts);
            if (!string.IsNullOrEmpty(err?.Error)) return err!.Error;
        }
        catch (Exception) { /* fall through to the generic text */ }
        return $"{fallback} (HTTP {(int)resp.StatusCode})";
    }

    private void SaveToken()
    {
        using var f = FileAccess.Open(TokenPath, FileAccess.ModeFlags.Write);
        f.StoreString(JsonSerializer.Serialize(new AuthResponse(Token, Username), JsonOpts));
    }

    private void LoadToken()
    {
        if (!FileAccess.FileExists(TokenPath)) return;
        using var f = FileAccess.Open(TokenPath, FileAccess.ModeFlags.Read);
        try
        {
            var saved = JsonSerializer.Deserialize<AuthResponse>(f.GetAsText(), JsonOpts);
            if (saved is null || string.IsNullOrEmpty(saved.Token)) return;
            Token = saved.Token;
            Username = saved.Username;
        }
        catch (Exception)
        {
            // corrupt token file: ignore, user logs in again
        }
    }
}
