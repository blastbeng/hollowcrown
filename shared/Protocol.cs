namespace Hollowcrown.Shared;

/// <summary>
/// Protocol DTOs shared between game client, match server, and central server.
/// Wire format is JSON; field names are the contract, do not rename carelessly.
/// </summary>
public record HealthResponse(string Status, string Version);

// ---- auth ----
public record AuthRequest(string User, string Pass);
public record AuthResponse(string Token, string Username);

// ---- characters ----
public record CreateCharacterRequest(string Name, string ClassId);
public record CharacterDto(int Id, string Name, string ClassId, int Level, int Xp, int Mmr, string GearJson);
public record ProgressRequest(int Level, int Xp, string GearJson);

// ---- server registry ----
public record ServerRegistration(string ServerId, string Name, string Mode, string Host, int Port, int Players, int MaxPlayers, bool HasPassword, string? Token = null);
public record ServerInfo(string ServerId, string Name, string Mode, string Host, int Port, int Players, int MaxPlayers, bool HasPassword);

// ---- MMR / ranking (Vision 8; match server reports, central owns) ----
public record MmrReport(string ServerToken, int ModeId, long WinnerCharacterId, long LoserCharacterId, int WinnerBefore, int LoserBefore);
public record MmrResult(int WinnerCharacterId, int LoserCharacterId, int WinnerMmr, int LoserMmr, int WinnerDelta, int LoserDelta, string WinnerTier, string LoserTier);
public record LeaderboardEntry(string Name, string ClassId, int Level, int Mmr, string Tier);

// ---- server tokens (Vision 4: match server identity) ----
public record ServerTokenResponse(string ServerId, string Token, string ExpiresAt);

// ---- errors ----
public record ErrorResponse(string Error);
