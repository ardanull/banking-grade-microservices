using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Events;
namespace Common;
public static class Rabbit{
  public static IConnection Connect(string amqpUrl){
    var f=new ConnectionFactory{ Uri=new Uri(amqpUrl), DispatchConsumersAsync=true, AutomaticRecoveryEnabled=true };
    return f.CreateConnection();
  }
  public static void EnsureExchange(IModel ch,string exchange="events"){
    ch.ExchangeDeclare(exchange, ExchangeType.Topic, durable:true, autoDelete:false);
  }
  public static void EnsureQueueBound(IModel ch,string queue,IEnumerable<string> bindings,string exchange="events"){
    EnsureExchange(ch, exchange);
    ch.QueueDeclare(queue, durable:true, exclusive:false, autoDelete:false);
    foreach(var rk in bindings) ch.QueueBind(queue, exchange, rk);
  }
  public static void PublishJson(IModel ch,string routingKey,object payload,string exchange="events"){
    EnsureExchange(ch, exchange);
    var body=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
    var props=ch.CreateBasicProperties(); props.ContentType="application/json"; props.DeliveryMode=2;
    ch.BasicPublish(exchange, routingKey, props, body);
  }
  public static async Task ConsumeJsonAsync(IModel ch,string queue,Func<string,JsonElement,Task> handler,CancellationToken ct){
    var consumer=new AsyncEventingBasicConsumer(ch);
    consumer.Received += async (_, ea) => {
      var json=JsonDocument.Parse(ea.Body.ToArray()).RootElement;
      await handler(ea.RoutingKey, json);
      ch.BasicAck(ea.DeliveryTag, false);
    };
    ch.BasicConsume(queue, autoAck:false, consumer);
    while(!ct.IsCancellationRequested) await Task.Delay(500, ct);
  }


public static void EnsureQueueBoundWithRetry(IModel ch, string queue, IEnumerable<string> bindings, int maxAttempts = 5, int baseDelaySeconds = 2, string exchange="events")
{
  EnsureExchange(ch, exchange);

  var dlx = $"{queue}.dlx";
  var dlq = $"{queue}.dlq";
  ch.ExchangeDeclare(dlx, ExchangeType.Fanout, durable: true, autoDelete: false);
  ch.QueueDeclare(dlq, durable: true, exclusive: false, autoDelete: false);
  ch.QueueBind(dlq, dlx, routingKey: "");

  var args = new Dictionary<string, object> { { "x-dead-letter-exchange", dlx } };
  ch.QueueDeclare(queue, durable: true, exclusive: false, autoDelete: false, arguments: args);

  var rex = $"{queue}.retry-ex";
  ch.ExchangeDeclare(rex, ExchangeType.Topic, durable: true, autoDelete: false);

  for (var attempt = 1; attempt <= maxAttempts - 1; attempt++)
  {
    var delay = baseDelaySeconds * (int)Math.Pow(2, attempt - 1);
    var rq = $"{queue}.retry.{attempt}";
    var rargs = new Dictionary<string, object>
    {
      { "x-message-ttl", delay * 1000 },
      { "x-dead-letter-exchange", exchange }
    };
    ch.QueueDeclare(rq, durable: true, exclusive: false, autoDelete: false, arguments: rargs);
    ch.QueueBind(rq, rex, routingKey: "#");
  }

  foreach (var rk in bindings) ch.QueueBind(queue, exchange, rk);
}

public static async Task ConsumeJsonWithRetryAsync(
  IModel ch,
  string queue,
  Func<string, string, JsonElement, Task> handler,
  int maxAttempts = 5,
  int baseDelaySeconds = 2,
  string exchange="events",
  CancellationToken ct = default)
{
  var consumer = new AsyncEventingBasicConsumer(ch);
  consumer.Received += async (_, ea) =>
  {
    var msgId = ea.BasicProperties?.MessageId ?? Convert.ToHexString(ea.Body.ToArray()).ToLowerInvariant();
    var attempt = 1;
    if (ea.BasicProperties?.Headers != null && ea.BasicProperties.Headers.TryGetValue("x-attempt", out var raw))
    {
      try
      {
        if (raw is byte[] b) attempt = int.Parse(Encoding.UTF8.GetString(b));
        else attempt = Convert.ToInt32(raw);
      }
      catch { attempt = 1; }
    }

    try
    {
      var json = JsonDocument.Parse(ea.Body.ToArray()).RootElement;
      await handler(ea.RoutingKey, msgId, json);
      ch.BasicAck(ea.DeliveryTag, multiple: false);
    }
    catch
    {
      if (attempt < maxAttempts)
      {
        var props = ch.CreateBasicProperties();
        props.ContentType = ea.BasicProperties?.ContentType ?? "application/json";
        props.DeliveryMode = 2;
        props.MessageId = msgId;
        props.Headers = ea.BasicProperties?.Headers ?? new Dictionary<string, object>();
        props.Headers["x-attempt"] = Encoding.UTF8.GetBytes((attempt + 1).ToString());

        var rex = $"{queue}.retry-ex";
        var rk = ea.RoutingKey ?? "#";
        ch.BasicPublish(rex, rk, props, ea.Body);
        ch.BasicAck(ea.DeliveryTag, multiple: false);
      }
      else
      {
        ch.BasicReject(ea.DeliveryTag, requeue: false);
      }
    }
  };

  ch.BasicConsume(queue, autoAck: false, consumer);
  while (!ct.IsCancellationRequested) await Task.Delay(500, ct);
}
}