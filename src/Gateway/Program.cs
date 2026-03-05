using Common;
using Dapper;
using Npgsql;
using RabbitMQ.Client;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);
((IHostApplicationBuilder)builder).AddOtel("gateway");
var pgUrl = Env.Require("PG_URL");
var redisUrl = Env.Require("REDIS_URL");
var amqpUrl = Env.Require("AMQP_URL");

builder.Services.AddSingleton(_ => Db.DataSource(pgUrl));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisUrl));
builder.Services.AddSingleton<IConnection>(_ => Rabbit.Connect(amqpUrl));
builder.Services.AddSingleton<IModel>(sp => { var ch=sp.GetRequiredService<IConnection>().CreateModel(); Rabbit.EnsureExchange(ch); return ch; });

var app = builder.Build();
await Migrations.RunAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), "/app/db/migrations");

app.MapGet("/health", () => Results.Ok(new { ok=true, service="gateway" }));

app.MapPost("/v1/transactions", async (HttpRequest req, NpgsqlDataSource ds, IModel ch, IConnectionMultiplexer mux, TransactionCreate body) =>
{
    // NOTE: Demo skeleton — expand with HMAC + replay protection + full idempotency caching if needed.
    var id = Guid.NewGuid().ToString("N");
    await using var conn = await ds.OpenConnectionAsync();
    await conn.ExecuteAsync("CREATE TABLE IF NOT EXISTS transactions(id TEXT PRIMARY KEY, user_id TEXT, amount NUMERIC, created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())");
    await conn.ExecuteAsync("INSERT INTO transactions(id,user_id,amount) VALUES(@Id,@U,@A)", new{ Id=id, U=body.UserId, A=body.Amount });
    Rabbit.PublishJson(ch, "transaction.created", new { id, userId=body.UserId, amount=body.Amount, createdAt=DateTime.UtcNow.ToString("O") });
    return Results.Accepted($"/v1/transactions/{id}", new { id });
});

app.Run();
record TransactionCreate(string UserId, decimal Amount);
