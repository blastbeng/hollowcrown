using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;
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

    public override void _Ready() => LoadToken();

    private static string BaseUrl() =>
        OS.GetEnvironment("HC_CENTRAL_URL") is { Length: > 0 } env ? env : DefaultBaseUrl;

    public async Task Register(string user, string pass) => await Auth("auth/register", user, pass);
    public async Task Login(string user, string pass) => await Auth("auth/login", user, pass);

    private async Task Auth(string path, string user, string pass)
    {
        try
        {
            using var resp = await Http.PostAsJsonAsync($"{BaseUrl()}/{path}", new AuthRequest(user, pass), JsonOpts);
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
        using var resp = await Authed(HttpMethod.Get, "characters");
        return resp.IsSuccessStatusCode
            ? await resp.Content.ReadFromJsonAsync<List<CharacterDto>>(JsonOpts)
            : null;
    }

    public async Task<CharacterDto?> CreateCharacter(string name, string classId)
    {
        using var resp = await Authed(HttpMethod.Post, "characters", new CreateCharacterRequest(name, classId));
        return resp.IsSuccessStatusCode
            ? await resp.Content.ReadFromJsonAsync<CharacterDto>(JsonOpts)
            : null;
    }

    public async Task<CharacterDto?> SaveProgress(int characterId, int level, int xp, string gearJson)
    {
        using var resp = await Authed(HttpMethod.Put, $"characters/{characterId}/progress",
            new ProgressRequest(level, xp, gearJson));
        return resp.IsSuccessStatusCode
            ? await resp.Content.ReadFromJsonAsync<CharacterDto>(JsonOpts)
            : null;
    }

    public async Task<List<ServerInfo>?> ListServers(string mode = "")
    {
        var query = mode.Length > 0 ? $"?mode={Uri.EscapeDataString(mode)}" : "";
        using var resp = await Authed(HttpMethod.Get, $"servers{query}");
        return resp.IsSuccessStatusCode
            ? await resp.Content.ReadFromJsonAsync<List<ServerInfo>>(JsonOpts)
            : null;
    }

    /// <summary>Match-server registry heartbeat (unauthenticated by design for now).</summary>
    public async Task<bool> Heartbeat(ServerRegistration reg)
    {
        try
        {
            using var resp = await Http.PostAsJsonAsync($"{BaseUrl()}/servers/heartbeat", reg, JsonOpts);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task<HttpResponseMessage> Authed(HttpMethod method, string path, object? body = null)
    {
        var req = new HttpRequestMessage(method, $"{BaseUrl()}/{path}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
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
