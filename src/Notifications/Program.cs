using Common;
using Dapper;
using Npgsql;
using RabbitMQ.Client;

var builder = Host.CreateApplicationBuilder(args);
Common.Telemetry.AddOtel(builder, "notifications");
builder.Services.AddSingleton(_ => Db.DataSource(Env.Require("PG_URL")));
builder.Services.AddSingleton<IConnection>(_ => Rabbit.Connect(Env.Require("AMQP_URL")));
builder.Services.AddHostedService<Worker>();
var host = builder.Build();
await host.RunAsync();

sealed class Worker : BackgroundService{
  private readonly IModel _ch; private readonly NpgsqlDataSource _ds;
  public Worker(IConnection conn, NpgsqlDataSource ds){ _ds=ds; _ch=conn.CreateModel(); Rabbit.EnsureQueueBound(_ch,"notifications.q",new[]{"alert.created","payment.created"}); }
  protected override async Task ExecuteAsync(CancellationToken stoppingToken){
    await Rabbit.ConsumeJsonAsync(_ch,"notifications.q", async (rk, json) => {
      await using var c = await _ds.OpenConnectionAsync(stoppingToken);
      await c.ExecuteAsync("CREATE TABLE IF NOT EXISTS notifications(id TEXT PRIMARY KEY, kind TEXT, created_at TIMESTAMPTZ NOT NULL DEFAULT NOW())");
      await c.ExecuteAsync("INSERT INTO notifications(id,kind) VALUES(@Id,@K)", new{ Id=Guid.NewGuid().ToString("N"), K=rk });
    }, stoppingToken);
  }
}
