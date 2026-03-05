using Common;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

if (args.Length < 2)
{
  Console.WriteLine("Usage: DlqReplayer <AMQP_URL> <QUEUE_BASE_NAME> [--rk <routingKey>]");
  return;
}

var amqpUrl = args[0];
var baseQueue = args[1];
var dlq = $"{baseQueue}.dlq";
var rkOverride = args.Length >= 4 && args[2] == "--rk" ? args[3] : null;

using var conn = Rabbit.Connect(amqpUrl);
using var ch = conn.CreateModel();

Console.WriteLine($"Replaying {dlq} -> events");

var consumer = new EventingBasicConsumer(ch);
consumer.Received += (_, ea) =>
{
  try
  {
    var props = ch.CreateBasicProperties();
    props.ContentType = ea.BasicProperties?.ContentType ?? "application/json";
    props.DeliveryMode = 2;
    props.MessageId = ea.BasicProperties?.MessageId ?? Guid.NewGuid().ToString();

    var rk = rkOverride ?? ea.RoutingKey ?? "#";
    ch.BasicPublish("events", rk, props, ea.Body);
    ch.BasicAck(ea.DeliveryTag, false);
  }
  catch
  {
    ch.BasicReject(ea.DeliveryTag, requeue: false);
  }
};

ch.BasicConsume(dlq, autoAck: false, consumer);
Console.WriteLine("Ctrl+C to stop.");
await Task.Delay(Timeout.Infinite);
