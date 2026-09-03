namespace Hollowcrown.Shared;

/// <summary>
/// Protocol DTOs shared between game client, match server, and central server.
/// Wire format is JSON; field names are the contract, do not rename carelessly.
/// </summary>
public record HealthResponse(string Status, string Version);
