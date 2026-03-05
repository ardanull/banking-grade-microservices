using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NUnit.Framework;

namespace Banking.Quality.Tests;

public class IntegrationTests
{
  static async Task WaitHealthy(string url, int seconds=120)
  {
    using var http = new HttpClient();
    var until = DateTime.UtcNow.AddSeconds(seconds);
    while (DateTime.UtcNow < until)
    {
      try { var r = await http.GetAsync(url); if (r.IsSuccessStatusCode) return; } catch {}
      await Task.Delay(1500);
    }
    Assert.Fail($"Not healthy: {url}");
  }

  [Test]
  public async Task Smoke_Flow()
  {
    await WaitHealthy("http://localhost:8081/health");
    await WaitHealthy("http://localhost:8083/health");
    await WaitHealthy("http://localhost:8085/health");
    await WaitHealthy("http://localhost:8082/health");

    using var http = new HttpClient();
    await http.PostAsync("http://localhost:8081/auth/seed-admin", new StringContent("{}", Encoding.UTF8, "application/json"));

    var login = new StringContent("{"email":"admin@demo.local","password":"Admin123!"}", Encoding.UTF8, "application/json");
    var lr = await http.PostAsync("http://localhost:8081/auth/login", login);
    Assert.That(lr.IsSuccessStatusCode, Is.True);
    var token = JsonDocument.Parse(await lr.Content.ReadAsStringAsync()).RootElement.GetProperty("accessToken").GetString()!;
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    var acc = new StringContent("{"userId":"usr_demo","currency":"TRY","initialBalance":25000}", Encoding.UTF8, "application/json");
    var ar = await http.PostAsync("http://localhost:8083/accounts", acc);
    Assert.That((int)ar.StatusCode, Is.EqualTo(201));
    var accId = JsonDocument.Parse(await ar.Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString()!;

    var pay = new HttpRequestMessage(HttpMethod.Post, "http://localhost:8085/payments");
    pay.Headers.Add("Idempotency-Key", "pay-it-001");
    pay.Content = new StringContent($"{{"fromAccountId":"{accId}","toIban":"TR000000000000000000000001","amount":1200,"currency":"TRY","channel":"MOBILE"}}", Encoding.UTF8, "application/json");
    var pr = await http.SendAsync(pay);
    Assert.That((int)pr.StatusCode, Is.EqualTo(202));

    await Task.Delay(1500);
    var al = await http.GetAsync("http://localhost:8082/alerts");
    Assert.That(al.IsSuccessStatusCode, Is.True);
  }
}
