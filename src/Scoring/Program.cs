using Common;
using RabbitMQ.Client;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);
Common.Telemetry.AddOtel(builder, "scoring");
builder.Services.AddSingleton<IConnection>(_ => Rabbit.Connect(Env.Require("AMQP_URL")));
builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(Env.Require("REDIS_URL")));
builder.Services.AddHostedService<Worker>();
var host = builder.Build();
await host.RunAsync();

sealed class Worker : BackgroundService{
  private readonly IModel _ch;
  public Worker(IConnection conn){ _ch=conn.CreateModel(); Rabbit.EnsureQueueBound(_ch, "scoring.q", new[]{"transaction.created"}); }
  protected override async Task ExecuteAsync(CancellationToken stoppingToken){
    await Rabbit.ConsumeJsonAsync(_ch, "scoring.q", async (_, json) => {
      // Skeleton: compute rules, publish alert.created
      Rabbit.PublishJson(_ch, "alert.created", new { id=Guid.NewGuid().ToString("N"), transactionId=json.GetProperty("id").GetString(), severity="HIGH" });
      await Task.CompletedTask;
    }, stoppingToken);
  }
}
