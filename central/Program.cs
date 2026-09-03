using Hollowcrown.Shared;

// Central server (accounts, characters, server registry, matchmaking, ranking).
// http://localhost:6560 by default (ASPNETCORE_URLS / --urls override).
// v0.1 bootstrap: process liveness only; auth + characters arrive in the next iteration.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/health", () => new HealthResponse("ok", "0.1.0"));

app.Run();
