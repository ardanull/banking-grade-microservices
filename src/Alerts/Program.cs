using Common;
using Dapper;
using Npgsql;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);
((IHostApplicationBuilder)builder).AddOtel("alerts");
builder.Services.AddSingleton(_ => Db.DataSource(Env.Require("PG_URL")));
builder.Services.AddSingleton<IConnection>(_ => Rabbit.Connect(Env.Require("AMQP_URL")));
builder.Services.AddHostedService<Consumer>();

var app = builder.Build();
await Migrations.RunAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), "/app/db/migrations");
app.MapGet("/health", () => Results.Ok(new { ok=true, service="alerts" }));
app.MapGet("/alerts", async (NpgsqlDataSource ds) => {
  await using var conn = await ds.OpenConnectionAsync();
  await conn.ExecuteAsync("CREATE TABLE IF NOT EXISTS alerts(id TEXT PRIMARY KEY, severity TEXT, created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())");
  var rows = await conn.QueryAsync("SELECT * FROM alerts ORDER BY created_at DESC LIMIT 200");
  return Results.Ok(new { items=rows });
});
app.Run();

sealed class Consumer : BackgroundService{
  private readonly IModel _ch; private readonly NpgsqlDataSource _ds;
  public Consumer(IConnection conn, NpgsqlDataSource ds){ _ds=ds; _ch=conn.CreateModel(); Rabbit.EnsureQueueBound(_ch, "alerts.q", new[]{"alert.created"}); }
  protected override async Task ExecuteAsync(CancellationToken stoppingToken){
    await Rabbit.ConsumeJsonAsync(_ch, "alerts.q", async (_, json) => {
      await using var c = await _ds.OpenConnectionAsync(stoppingToken);
      await c.ExecuteAsync("CREATE TABLE IF NOT EXISTS alerts(id TEXT PRIMARY KEY, severity TEXT, created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())");
      await c.ExecuteAsync("INSERT INTO alerts(id,severity) VALUES(@Id,@S) ON CONFLICT DO NOTHING",
        new{ Id=json.GetProperty("id").GetString(), S=json.GetProperty("severity").GetString() });
    }, stoppingToken);
  }
}
