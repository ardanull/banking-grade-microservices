using Common;
using Dapper;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
((IHostApplicationBuilder)builder).AddOtel("accounts");
var pgUrl = Env.Require("PG_URL");
builder.Services.AddSingleton(_ => Db.DataSource(pgUrl));
var app = builder.Build();
await Migrations.RunAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), "/app/db/migrations");

app.MapGet("/health", () => Results.Ok(new { ok=true, service="accounts" }));
app.MapPost("/accounts", async (NpgsqlDataSource ds, CreateAccount req) =>
{
    await using var conn = await ds.OpenConnectionAsync();
    await conn.ExecuteAsync("CREATE TABLE IF NOT EXISTS accounts(id TEXT PRIMARY KEY, user_id TEXT, balance NUMERIC, created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())");
    var id=Guid.NewGuid().ToString("N");
    await conn.ExecuteAsync("INSERT INTO accounts(id,user_id,balance) VALUES(@Id,@U,@B)", new{ Id=id, U=req.UserId, B=req.InitialBalance });
    return Results.Created($"/accounts/{id}", new { id });
});
app.Run();
record CreateAccount(string UserId, decimal InitialBalance);
