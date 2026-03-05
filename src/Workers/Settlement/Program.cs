using Common;
using RabbitMQ.Client;

var builder=Host.CreateApplicationBuilder(args);
var pgUrl=Env.Require("PG_URL");
var amqpUrl=Env.Require("AMQP_URL");
var serviceName=Env.Get("AUDIT_SERVICE_NAME","settlement");

builder.Services.AddSingleton(_=>Db.DataSource(pgUrl));
builder.Services.AddSingleton<IConnection>(_=>Rabbit.Connect(amqpUrl));
builder.Services.AddHostedService(sp=> new Worker.Settlement.Worker(
  sp.GetRequiredService<ILogger<Worker.Settlement.Worker>>(),
  sp.GetRequiredService<Npgsql.NpgsqlDataSource>(),
  sp.GetRequiredService<IConnection>(),
  serviceName
));
var host=builder.Build();
await Migrations.RunAsync(host.Services.GetRequiredService<Npgsql.NpgsqlDataSource>(), "/app/db/migrations");
await host.RunAsync();
