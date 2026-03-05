using System.Text.Json;
using NUnit.Framework;

namespace Banking.Quality.Tests;

public class ContractTests
{
  [Test]
  public void Contracts_Load()
  {
    var path = Path.Combine(TestContext.CurrentContext.TestDirectory, "contracts", "events.json");
    var json = File.ReadAllText(path);
    using var doc = JsonDocument.Parse(json);
    Assert.That(doc.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
  }
}
