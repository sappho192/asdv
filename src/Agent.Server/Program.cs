using Agent.Server.Models;
using Agent.Server.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { name = "Agent.Server", status = "ok" }));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/sessions", (CreateSessionRequest request, ISessionStore store) =>
{
    var session = store.Create(request);
    return Results.Ok(new CreateSessionResponse(session.Id));
});

app.MapGet("/api/sessions/{id}", (string id, ISessionStore store) =>
{
    return store.TryGet(id, out var session)
        ? Results.Ok(session)
        : Results.NotFound();
});

app.MapPost("/api/sessions/{id}/chat", (string id, ChatRequest request) =>
{
    return Results.Problem(
        detail: "Chat orchestration is not wired yet.",
        statusCode: StatusCodes.Status501NotImplemented);
});

app.MapGet("/api/sessions/{id}/stream", (string id) =>
{
    return Results.Problem(
        detail: "SSE streaming is not wired yet.",
        statusCode: StatusCodes.Status501NotImplemented);
});

app.Run();
