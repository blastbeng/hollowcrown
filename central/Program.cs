using System.Text.Json;
using System.Text.Json.Serialization;
using Hollowcrown.Central;
using Hollowcrown.Shared;
using Microsoft.Data.Sqlite;

// Central server (accounts, characters, server registry, matchmaking, ranking).
// http://localhost:6560 by default (ASPNETCORE_URLS / --urls override).
// v0.2: auth (PBKDF2 salted hashes, bearer tokens), characters CRUD, SQLite,
// heartbeat server registry with 30 s TTL (match-server tokens, Vision 4),
// Elo/MMR reporting + leaderboard (Vision 8, Rating.cs).

var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
};

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

Db.Init(app.Configuration["HC_CENTRAL_DB"]);

// ---------- helpers ----------
static IResult Fail(int status, string message) => Results.Json(new ErrorResponse(message), statusCode: status);

// Resolves the bearer token to (userId, username); null when invalid/expired.
static (long UserId, string Username)? ResolveUser(HttpRequest req, SqliteConnection conn)
{
    var header = req.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
    var token = header["Bearer ".Length..].Trim();
    if (token.Length == 0) return null;

    using var cmd = conn.Command(
        "SELECT u.id, u.username, t.expires_at FROM tokens t JOIN users u ON u.id = t.user_id WHERE t.token = $t",
        ("$t", token));
    using var reader = cmd.ExecuteReader();
    if (!reader.Read()) return null;
    if (!DateTime.TryParse(reader.GetString(2), null, System.Globalization.DateTimeStyles.RoundtripKind, out var expires)
        || expires <= DateTime.UtcNow) return null;
    return (reader.GetInt64(0), reader.GetString(1));
}

// Resolves the bearer token to a REGISTERED match server; null when unknown.
// Vision 4: heartbeats, progression PUTs and MMR reports must come from the
// match server, not from any anonymous client.
static string? ResolveServer(HttpRequest req, SqliteConnection conn)
{
    var header = req.Headers.Authorization.ToString();
    if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;
    var token = header["Bearer ".Length..].Trim();
    if (token.Length == 0) return null;
    var serverId = conn.Scalar("SELECT server_id FROM servers WHERE token = $t", ("$t", token));
    return serverId is string s ? s : null;
}

static bool OwnsCharacter(SqliteConnection conn, long userId, int characterId)
    => Convert.ToInt64(conn.Scalar(
           "SELECT COUNT(*) FROM characters WHERE id = $id AND user_id = $u",
           ("$id", characterId), ("$u", userId))) > 0;

static CharacterDto ReadCharacter(SqliteDataReader r) => new(
    r.GetInt32(0), r.GetString(1), r.GetString(2),
    r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetString(6));

const string SelectCharacter =
    "SELECT id, name, class_id, level, xp, mmr, gear_json FROM characters WHERE id = $id";

const int MaxLevel = 100;          // sane caps: deeper anti-cheat is a later task (vision Section 4)
const long MaxXp = 100_000_000;

// ---------- auth ----------
app.MapPost("/auth/register", (AuthRequest r) =>
{
    if (string.IsNullOrWhiteSpace(r.User) || r.User.Length is < 3 or > 32)
        return Fail(400, "username must be 3-32 characters");
    if (string.IsNullOrEmpty(r.Pass) || r.Pass.Length < 6)
        return Fail(400, "password must be at least 6 characters");

    using var conn = Db.Open();
    var (salt, hash) = PasswordHasher.Hash(r.Pass);
    try
    {
        conn.Exec("INSERT INTO users(username, pass_salt, pass_hash, created_at) VALUES ($u, $s, $h, $c)",
            ("$u", r.User), ("$s", salt), ("$h", hash), ("$c", Db.Now()));
    }
    catch (SqliteException)
    {
        return Fail(409, "username already taken");
    }

    var token = PasswordHasher.NewToken();
    // Opportunistic housekeeping: expired tokens never accumulate.
    conn.Exec("DELETE FROM tokens WHERE expires_at <= $now", ("$now", Db.Now()));
    conn.Exec("INSERT INTO tokens(token, user_id, expires_at) VALUES ($t, (SELECT id FROM users WHERE username = $u), $e)",
        ("$t", token), ("$u", r.User), ("$e", DateTime.UtcNow.AddDays(7).ToString("o")));
    return Results.Json(new AuthResponse(token, r.User), options: jsonOptions);
});

app.MapPost("/auth/login", (AuthRequest r) =>
{
    using var conn = Db.Open();
    using (var cmd = conn.Command("SELECT id, pass_salt, pass_hash FROM users WHERE username = $u", ("$u", r.User)))
    {
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return Fail(401, "invalid username or password");
        var salt = (byte[])reader.GetValue(1);
        var hash = (byte[])reader.GetValue(2);
        if (!PasswordHasher.Verify(r.Pass, salt, hash))
            return Fail(401, "invalid username or password");
    }

    var token = PasswordHasher.NewToken();
    conn.Exec("DELETE FROM tokens WHERE expires_at <= $now", ("$now", Db.Now()));
    conn.Exec("INSERT INTO tokens(token, user_id, expires_at) VALUES ($t, (SELECT id FROM users WHERE username = $u), $e)",
        ("$t", token), ("$u", r.User), ("$e", DateTime.UtcNow.AddDays(7).ToString("o")));
    return Results.Json(new AuthResponse(token, r.User), options: jsonOptions);
});

// ---------- characters ----------
app.MapGet("/characters", (HttpRequest req) =>
{
    using var conn = Db.Open();
    var user = ResolveUser(req, conn);
    if (user is null) return Fail(401, "unauthorized");

    using var cmd = conn.Command(
        "SELECT id, name, class_id, level, xp, mmr, gear_json FROM characters WHERE user_id = $u ORDER BY id",
        ("$u", user.Value.UserId));
    using var reader = cmd.ExecuteReader();
    var list = new List<CharacterDto>();
    while (reader.Read()) list.Add(ReadCharacter(reader));
    return Results.Json(list, options: jsonOptions);
});

app.MapPost("/characters", (HttpRequest req, CreateCharacterRequest r) =>
{
    if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length is < 2 or > 24)
        return Fail(400, "character name must be 2-24 characters");
    if (string.IsNullOrWhiteSpace(r.ClassId))
        return Fail(400, "classId is required");

    using var conn = Db.Open();
    var user = ResolveUser(req, conn);
    if (user is null) return Fail(401, "unauthorized");

    long newId;
    try
    {
        conn.Exec("INSERT INTO characters(user_id, name, class_id, created_at) VALUES ($u, $n, $c, $t)",
            ("$u", user.Value.UserId), ("$n", r.Name), ("$c", r.ClassId), ("$t", Db.Now()));
        newId = conn.LastInsertRowId();
    }
    catch (SqliteException)
    {
        return Fail(409, "you already have a character with that name");
    }

    using var cmd = conn.Command(SelectCharacter, ("$id", newId));
    using var reader = cmd.ExecuteReader();
    reader.Read();
    return Results.Json(ReadCharacter(reader), options: jsonOptions, statusCode: 201);
});

app.MapGet("/characters/{id}", (HttpRequest req, int id) =>
{
    using var conn = Db.Open();
    var user = ResolveUser(req, conn);
    if (user is null) return Fail(401, "unauthorized");
    if (!OwnsCharacter(conn, user.Value.UserId, id)) return Fail(404, "character not found");

    using var cmd = conn.Command(SelectCharacter, ("$id", id));
    using var reader = cmd.ExecuteReader();
    reader.Read();
    return Results.Json(ReadCharacter(reader), options: jsonOptions);
});

app.MapPut("/characters/{id}/progress", (HttpRequest req, int id, ProgressRequest r) =>
{
    using var conn = Db.Open();
    var user = ResolveUser(req, conn);
    if (user is null) return Fail(401, "unauthorized");
    if (!OwnsCharacter(conn, user.Value.UserId, id)) return Fail(404, "character not found");
    // Vision 4: progression is MATCH-SERVER-owned data — the PUT must carry
    // the server token (the client relays it, but never invents it).
    if (ResolveServer(req, conn) is null)
        return Fail(403, "match server token required for progression saves");

    // validate reports against sane caps (vision Section 4 anti-cheat stance)
    var level = Math.Clamp(r.Level, 1, MaxLevel);
    var xp = Math.Clamp(r.Xp, 0, MaxXp);

    conn.Exec("UPDATE characters SET level = $l, xp = $x, gear_json = $g WHERE id = $id",
        ("$l", level), ("$x", xp), ("$g", string.IsNullOrWhiteSpace(r.GearJson) ? "[]" : r.GearJson), ("$id", id));

    using var cmd = conn.Command(SelectCharacter, ("$id", id));
    using var reader = cmd.ExecuteReader();
    reader.Read();
    return Results.Json(ReadCharacter(reader), options: jsonOptions);
});

// ---------- server registry (30 s TTL; Vision 4 match-server tokens) ----------
// Registration mints a server token — the match server's identity for
// heartbeats, progression saves and MMR reports. Tokens are 64 hex chars,
// stored plaintext for v1 (same stance as user tokens; hardening later).
app.MapPost("/servers/register", (ServerRegistration r) =>
{
    if (string.IsNullOrWhiteSpace(r.ServerId) || string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.Mode))
        return Fail(400, "serverId, name and mode are required");
    if (r.Port is < 1 or > 65535) return Fail(400, "port must be 1-65535");
    if (r.ServerId.Length > 64 || r.Name.Length > 64 || r.Mode.Length > 16 ||
        r.Host is { Length: > 64 })
        return Fail(400, "serverId/name/mode/host exceed length limits");

    using var conn = Db.Open();
    // A re-register refreshes the entry (and the token) — the same realm
    // restarting must not orphan its registry row.
    conn.Exec("""
        INSERT INTO servers(server_id, name, mode, host, port, players, max_players, has_password, last_seen, token)
        VALUES ($sid, $n, $m, $h, $p, $pl, $mx, $pw, $t, $tok)
        ON CONFLICT(server_id) DO UPDATE SET
            name = $n, mode = $m, host = $h, port = $p,
            players = $pl, max_players = $mx, has_password = $pw, last_seen = $t, token = $tok
        """,
        ("$sid", r.ServerId), ("$n", r.Name), ("$m", r.Mode), ("$h", string.IsNullOrWhiteSpace(r.Host) ? "127.0.0.1" : r.Host),
        ("$p", r.Port), ("$pl", 0), ("$mx", Math.Clamp(r.MaxPlayers, 1, 1000)),
        ("$pw", r.HasPassword ? 1 : 0), ("$t", Db.Now()),
        ("$tok", PasswordHasher.NewToken()));
    var token = (string)conn.Scalar("SELECT token FROM servers WHERE server_id = $sid", ("$sid", r.ServerId))!;
    return Results.Json(new ServerTokenResponse(r.ServerId, token, Db.Now()), options: jsonOptions);
});

app.MapPost("/servers/heartbeat", (HttpRequest req, ServerRegistration r) =>
{
    if (string.IsNullOrWhiteSpace(r.ServerId) || string.IsNullOrWhiteSpace(r.Name) || string.IsNullOrWhiteSpace(r.Mode))
        return Fail(400, "serverId, name and mode are required");
    if (r.Port is < 1 or > 65535) return Fail(400, "port must be 1-65535");
    if (r.ServerId.Length > 64 || r.Name.Length > 64 || r.Mode.Length > 16 ||
        r.Host is { Length: > 64 })
        return Fail(400, "serverId/name/mode/host exceed length limits");

    using var conn = Db.Open();
    // Token identity (Vision 4): the token must match the registered realm.
    if (ResolveServer(req, conn) is not { } serverId || serverId != r.ServerId)
        return Fail(403, "unrecognized server token");

    conn.Exec("""
        INSERT INTO servers(server_id, name, mode, host, port, players, max_players, has_password, last_seen, token)
        VALUES ($sid, $n, $m, $h, $p, $pl, $mx, $pw, $t, $tok)
        ON CONFLICT(server_id) DO UPDATE SET
            name = $n, mode = $m, host = $h, port = $p,
            players = $pl, max_players = $mx, has_password = $pw, last_seen = $t
        """,
        ("$sid", r.ServerId), ("$n", r.Name), ("$m", r.Mode), ("$h", string.IsNullOrWhiteSpace(r.Host) ? "127.0.0.1" : r.Host),
        ("$p", r.Port), ("$pl", Math.Clamp(r.Players, 0, 1000)), ("$mx", Math.Clamp(r.MaxPlayers, 1, 1000)),
        ("$pw", r.HasPassword ? 1 : 0), ("$t", Db.Now()),
        ("$tok", (string?)conn.Scalar("SELECT COALESCE((SELECT token FROM servers WHERE server_id = $sid), '')", ("$sid", r.ServerId))));
    return Results.Json(new { ok = true }, options: jsonOptions);
});

app.MapGet("/servers", (HttpRequest req) =>
{
    var mode = req.Query["mode"].ToString();
    using var conn = Db.Open();
    var cutoff = DateTime.UtcNow.AddSeconds(-30).ToString("o");

    using var cmd = string.IsNullOrWhiteSpace(mode)
        ? conn.Command("SELECT server_id, name, mode, host, port, players, max_players, has_password FROM servers WHERE last_seen >= $c ORDER BY players DESC",
            ("$c", cutoff))
        : conn.Command("SELECT server_id, name, mode, host, port, players, max_players, has_password FROM servers WHERE last_seen >= $c AND mode = $m ORDER BY players DESC",
            ("$c", cutoff), ("$m", mode));
    using var reader = cmd.ExecuteReader();
    var list = new List<ServerInfo>();
    while (reader.Read())
    {
        list.Add(new ServerInfo(
            reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6),
            reader.GetInt32(7) != 0));
    }
    return Results.Json(list, options: jsonOptions);
});

// ---------- ranking: MMR report + leaderboard (Vision 8) ----------
// Only a REGISTERED match server may report a result (Vision 4 authority:
// clients never trust themselves). One Elo per mode; duel is mode 0 for now.
// Sane caps on reports: sane floors/ceilings instead of raw client numbers.
app.MapPost("/mmr/report", (HttpRequest req, MmrReport r) =>
{
    using var conn = Db.Open();
    if (ResolveServer(req, conn) is null)
        return Fail(403, "match server token required");
    if (r.WinnerCharacterId <= 0 || r.LoserCharacterId <= 0 || r.WinnerCharacterId == r.LoserCharacterId)
        return Fail(400, "two distinct positive character ids required");

    // Both characters must exist; ratings come from the DB, never the report
    // (an untrusted client could otherwise rewrite its own rating).
    using var cmd = conn.Command(
        "SELECT id, mmr FROM characters WHERE id IN ($w, $l)",
        ("$w", r.WinnerCharacterId), ("$l", r.LoserCharacterId));
    using var reader = cmd.ExecuteReader();
    long? winnerId = null, loserId = null;
    int winnerMmr = 0, loserMmr = 0;
    while (reader.Read())
    {
        var id = reader.GetInt64(0);
        if (id == r.WinnerCharacterId) { winnerId = id; winnerMmr = reader.GetInt32(1); }
        if (id == r.LoserCharacterId) { loserId = id; loserMmr = reader.GetInt32(1); }
    }
    if (winnerId is null || loserId is null)
        return Fail(404, "character not found");

    var (winDelta, loseDelta) = Hollowcrown.Shared.Rating.Update(winnerMmr, loserMmr);
    int winnerAfter = Hollowcrown.Shared.Rating.Apply(winnerMmr, winDelta);
    int loserAfter = Hollowcrown.Shared.Rating.Apply(loserMmr, loseDelta);
    conn.Exec("UPDATE characters SET mmr = $m WHERE id IN ($w, $l)",
        ("$m", winnerAfter), ("$w", winnerId), ("$l", loserId));

    return Results.Json(new MmrResult(
        (int)winnerId.Value, (int)loserId.Value, winnerAfter, loserAfter,
        winnerAfter - winnerMmr, loserAfter - loserMmr,
        Hollowcrown.Shared.Rating.TierOf(winnerAfter),
        Hollowcrown.Shared.Rating.TierOf(loserAfter)), options: jsonOptions);
});

app.MapGet("/leaderboard", (HttpRequest req) =>
{
    // Per-mode MMR (Vision 8) — duel is the only scored mode for now, so the
    // single rating column serves every mode until skirmish/open score.
    using var conn = Db.Open();
    using var cmd = conn.Command(
        "SELECT name, class_id, level, mmr FROM characters ORDER BY mmr DESC, xp DESC LIMIT 50");
    using var reader = cmd.ExecuteReader();
    var list = new List<LeaderboardEntry>();
    while (reader.Read())
    {
        int mmr = reader.GetInt32(3);
        list.Add(new LeaderboardEntry(
            reader.GetString(0), reader.GetString(1), reader.GetInt32(2), mmr,
            Hollowcrown.Shared.Rating.TierOf(mmr)));
    }
    return Results.Json(list, options: jsonOptions);
});

// ---------- process liveness ----------
app.MapGet("/health", () => Results.Json(new HealthResponse("ok", "0.3.0"), options: jsonOptions));

app.Run();
