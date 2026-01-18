using Agent.Server.Models;
using Agent.Server.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<SessionRuntimeFactory>();
builder.Services.AddSingleton<ISessionStore, InMemorySessionStore>();
builder.Services.AddSingleton<SessionRunner>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { name = "Agent.Server", status = "ok" }));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/sessions", (CreateSessionRequest request, ISessionStore store) =>
{
    var session = store.Create(request);
    return Results.Ok(new CreateSessionResponse(session.Info.Id));
});

app.MapGet("/api/sessions/{id}", (string id, ISessionStore store) =>
{
    return store.TryGet(id, out var session)
        ? Results.Ok(session.Info)
        : Results.NotFound();
});

app.MapPost("/api/sessions/{id}/chat", (string id, ChatRequest request, ISessionStore store, SessionRunner runner) =>
{
    if (!store.TryGet(id, out var session))
    {
        return Results.NotFound();
    }

    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "Message is required." });
    }

    _ = runner.RunAsync(session, request.Message, CancellationToken.None);
    return Results.Accepted();
});

app.MapPost("/api/sessions/{id}/approvals/{callId}", (string id, string callId, ApprovalRequest request, ISessionStore store) =>
{
    if (!store.TryGet(id, out var session))
    {
        return Results.NotFound();
    }

    var resolved = session.ApprovalService.TryResolve(callId, request.Approved);
    return resolved ? Results.Ok() : Results.NotFound();
});

app.MapGet("/api/sessions/{id}/stream", async (string id, ISessionStore store, HttpContext context) =>
{
    if (!store.TryGet(id, out var session))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    if (!session.TryOpenStream())
    {
        context.Response.StatusCode = StatusCodes.Status409Conflict;
        return;
    }

    context.Response.Headers.CacheControl = "no-cache";
    context.Response.Headers.Connection = "keep-alive";
    context.Response.ContentType = "text/event-stream";

    var options = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    try
    {
        await foreach (var evt in session.Events.Reader.ReadAllAsync(context.RequestAborted))
        {
            var json = JsonSerializer.Serialize(evt, options);
            await context.Response.WriteAsync($"event: {evt.Type}\n");
            await context.Response.WriteAsync($"data: {json}\n\n");
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }
    finally
    {
        session.CloseStream();
    }
});

app.Run();
