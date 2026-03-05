using Common;
using RabbitMQ.Client;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
((IHostApplicationBuilder)builder).AddOtel("payments");
builder.Services.AddSingleton(_ => Db.DataSource(Env.Require("PG_URL")));
builder.Services.AddSingleton<IConnection>(_ => Rabbit.Connect(Env.Require("AMQP_URL")));
builder.Services.AddSingleton<IModel>(sp => { var ch=sp.GetRequiredService<IConnection>().CreateModel(); Rabbit.EnsureExchange(ch); return ch; });

var app = builder.Build();
await Migrations.RunAsync(app.Services.GetRequiredService<NpgsqlDataSource>(), "/app/db/migrations");

app.MapGet("/health", () => Results.Ok(new { ok=true, service="payments" }));
app.MapPost("/payments", (IModel ch, CreatePayment req) => {
  // Skeleton: add idempotency + holds + outbox
  Rabbit.PublishJson(ch, "payment.created", new { id=Guid.NewGuid().ToString("N"), amount=req.Amount, createdAt=DateTime.UtcNow.ToString("O") });
  return Results.Accepted(new { ok=true });
});
app.Run();
record CreatePayment(decimal Amount);
