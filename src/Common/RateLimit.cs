using StackExchange.Redis;

namespace Common;

public sealed class RedisRateLimiter
{
  private readonly IDatabase _db;
  private readonly int _rps;
  public RedisRateLimiter(IDatabase db, int rps){ _db=db; _rps=Math.Max(1,rps); }

  public async Task<bool> AllowAsync(string key)
  {
    var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    var k = $"rl:{key}:{bucket}";
    var n = await _db.StringIncrementAsync(k);
    if (n == 1) await _db.KeyExpireAsync(k, TimeSpan.FromSeconds(2));
    return n <= _rps;
  }
}
