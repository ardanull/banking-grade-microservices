using System.Text.Json;
using Common;
using Dapper;
using Npgsql;
using RabbitMQ.Client;

namespace Worker.Settlement;

public sealed class Worker : BackgroundService{
  private readonly ILogger<Worker> _log;
  private readonly NpgsqlDataSource _ds;
  private readonly IModel _ch;
  private readonly string _serviceName;

  public Worker(ILogger<Worker> log, NpgsqlDataSource ds, IConnection conn, string serviceName){
    _log=log; _ds=ds; _serviceName=serviceName;
    _ch=conn.CreateModel();
    Rabbit.EnsureQueueBoundWithRetry(_ch, "settlement.q", new[]{"payment.created"}, maxAttempts:5, baseDelaySeconds:2);
    Rabbit.EnsureExchange(_ch);
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken){
    _log.LogInformation("Settlement worker up");
    await Rabbit.ConsumeJsonWithRetryAsync(_ch, "settlement.q", async (rk, msgId, json) => {
      await using var conn=await _ds.OpenConnectionAsync(stoppingToken);
      await using var tx=await conn.BeginTransactionAsync(stoppingToken);

      if(!await Inbox.TryRecordAsync(conn, "settlement", msgId, rk)){
        await tx.CommitAsync(stoppingToken);
        return;
      }

      var paymentId=json.GetProperty("id").GetString()!;
      var fromAcc=json.GetProperty("fromAccountId").GetString()!;
      var toIban=json.GetProperty("toIban").GetString()!;
      var amount=json.GetProperty("amount").GetDecimal();
      var currency=json.GetProperty("currency").GetString() ?? "TRY";
      var holdId=json.GetProperty("holdId").GetString()!;

      var p=await conn.QuerySingleOrDefaultAsync<dynamic>("SELECT status FROM payments WHERE id=@I FOR UPDATE", new{ I=paymentId }, tx);
      if(p is null){ await tx.CommitAsync(stoppingToken); return; }
      if((string)p.status is "SETTLED" or "FAILED"){ await tx.CommitAsync(stoppingToken); return; }

      if(!toIban.StartsWith("TR")){
        await conn.ExecuteAsync("UPDATE payments SET status='FAILED', failure_reason='IBAN_REJECT', updated_at=NOW() WHERE id=@I", new{ I=paymentId }, tx);
        await conn.ExecuteAsync("UPDATE holds SET status='RELEASED', updated_at=NOW() WHERE id=@H", new{ H=holdId }, tx);
        await conn.ExecuteAsync("UPDATE accounts SET available_balance=available_balance+@Amt, updated_at=NOW() WHERE id=@A", new{ A=fromAcc, Amt=amount }, tx);
        await tx.CommitAsync(stoppingToken);
        Rabbit.PublishJson(_ch, "payment.failed", new{ id=paymentId, reason="IBAN_REJECT" });
        await Audit.AppendAsync(_ds, new Audit.Entry(_serviceName, null, "PAYMENT_FAILED", "payment", paymentId, new{ reason="IBAN_REJECT" }, $"settle-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"), stoppingToken);
        return;
      }

      var clearing="acc_clearing_try";
      await conn.ExecuteAsync(@"INSERT INTO accounts(id,user_id,currency,available_balance,ledger_balance,status) VALUES(@I,'system',@C,0,0,'ACTIVE')
                               ON CONFLICT(id) DO NOTHING", new{ I=clearing, C=currency }, tx);

      var jid=Ids.New("jrnl");
      await conn.ExecuteAsync("INSERT INTO ledger_journals(id,reference_type,reference_id,currency) VALUES(@I,'payment',@R,@C)", new{ I=jid, R=paymentId, C=currency }, tx);
      await conn.ExecuteAsync("INSERT INTO ledger_entries(id,journal_id,account_id,direction,amount) VALUES(@I,@J,@A,'DEBIT',@Amt)", new{ I=Ids.New("le"), J=jid, A=fromAcc, Amt=amount }, tx);
      await conn.ExecuteAsync("INSERT INTO ledger_entries(id,journal_id,account_id,direction,amount) VALUES(@I,@J,@A,'CREDIT',@Amt)", new{ I=Ids.New("le"), J=jid, A=clearing, Amt=amount }, tx);
      await conn.ExecuteAsync("UPDATE accounts SET ledger_balance=ledger_balance-@Amt, updated_at=NOW() WHERE id=@A", new{ A=fromAcc, Amt=amount }, tx);
      await conn.ExecuteAsync("UPDATE accounts SET ledger_balance=ledger_balance+@Amt, updated_at=NOW() WHERE id=@A", new{ A=clearing, Amt=amount }, tx);

      await conn.ExecuteAsync("UPDATE holds SET status='CAPTURED', updated_at=NOW() WHERE id=@H", new{ H=holdId }, tx);
      await conn.ExecuteAsync("UPDATE payments SET status='SETTLED', journal_id=@J, updated_at=NOW() WHERE id=@I", new{ I=paymentId, J=jid }, tx);

      await tx.CommitAsync(stoppingToken);

      Rabbit.PublishJson(_ch, "payment.settled", new{ id=paymentId, journalId=jid });
      await Audit.AppendAsync(_ds, new Audit.Entry(_serviceName, null, "PAYMENT_SETTLED", "payment", paymentId, new{ journalId=jid }, $"settle-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"), stoppingToken);
    }, stoppingToken);
  }
}
