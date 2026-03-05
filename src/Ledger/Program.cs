using Common;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
((IHostApplicationBuilder)builder).AddOtel("ledger");
builder.Services.AddSingleton(_ => Db.DataSource(Env.Require("PG_URL")));
var app = builder.Build();
await Migrations.RunAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), "/app/db/migrations");
app.MapGet("/health", () => Results.Ok(new { ok=true, service="ledger" }));
app.MapPost("/journals", () => Results.Ok(new { ok=true, note="Skeleton: add double-entry journals + posting" }));
app.Run();
