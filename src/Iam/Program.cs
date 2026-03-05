using Common;
using Dapper;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
((IHostApplicationBuilder)builder).AddOtel("iam");
var pgUrl = Env.Require("PG_URL");
builder.Services.AddSingleton(_ => Db.DataSource(pgUrl));
var app = builder.Build();
await Migrations.RunAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), "/app/db/migrations");

app.MapGet("/health", () => Results.Ok(new { ok=true, service="iam" }));
app.MapPost("/auth/seed-admin", () => Results.Ok(new { ok=true, note="Skeleton: add roles/users + JWT issuance" }));
app.Run();
