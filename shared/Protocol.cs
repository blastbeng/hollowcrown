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
public record ServerRegistration(string ServerId, string Name, string Mode, string Host, int Port, int Players, int MaxPlayers, bool HasPassword);
public record ServerInfo(string ServerId, string Name, string Mode, string Host, int Port, int Players, int MaxPlayers, bool HasPassword);

// ---- errors ----
public record ErrorResponse(string Error);
