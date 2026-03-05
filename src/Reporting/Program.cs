using Common;
using Dapper;
using Npgsql;
using RabbitMQ.Client;

var builder = Host.CreateApplicationBuilder(args);
Common.Telemetry.AddOtel(builder, "reporting");
builder.Services.AddSingleton(_ => Db.DataSource(Env.Require("PG_URL")));
builder.Services.AddSingleton<IConnection>(_ => Rabbit.Connect(Env.Require("AMQP_URL")));
builder.Services.AddHostedService<Worker>();
var host = builder.Build();
await host.RunAsync();

sealed class Worker : BackgroundService{
  private readonly IModel _ch; private readonly NpgsqlDataSource _ds;
  public Worker(IConnection conn, NpgsqlDataSource ds){ _ds=ds; _ch=conn.CreateModel(); Rabbit.EnsureQueueBound(_ch,"reporting.q",new[]{"transaction.created"}); }
  protected override async Task ExecuteAsync(CancellationToken stoppingToken){
    await Rabbit.ConsumeJsonAsync(_ch,"reporting.q", async (_, json) => {
      var day=DateTime.UtcNow.Date;
      await using var c = await _ds.OpenConnectionAsync(stoppingToken);
      await c.ExecuteAsync("CREATE TABLE IF NOT EXISTS report_daily_volume(day DATE PRIMARY KEY, tx_count BIGINT NOT NULL)");
      await c.ExecuteAsync("INSERT INTO report_daily_volume(day,tx_count) VALUES(@D,1) ON CONFLICT(day) DO UPDATE SET tx_count=report_daily_volume.tx_count+1", new{ D=day });
    }, stoppingToken);
  }
}
