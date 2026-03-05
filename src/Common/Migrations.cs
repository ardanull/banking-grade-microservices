using Dapper;
using Npgsql;
namespace Common;
public static class Migrations{
  public static async Task RunAsync(NpgsqlDataSource ds,string dir,CancellationToken ct=default){
    await using var conn=await ds.OpenConnectionAsync(ct);
    await conn.ExecuteAsync("CREATE TABLE IF NOT EXISTS _migrations (id TEXT PRIMARY KEY, applied_at TIMESTAMPTZ NOT NULL DEFAULT NOW())");
    foreach(var file in Directory.EnumerateFiles(dir,"*.sql").OrderBy(x=>x)){
      var id=Path.GetFileName(file);
      var exists=await conn.ExecuteScalarAsync<int>("SELECT 1 FROM _migrations WHERE id=@id LIMIT 1", new{ id });
      if(exists==1) continue;
      var sql=await File.ReadAllTextAsync(file, ct);
      await conn.ExecuteAsync(sql);
      await conn.ExecuteAsync("INSERT INTO _migrations(id) VALUES(@id)", new{ id });
      Console.WriteLine($"[migrate] applied {id}");
    }
  }
}
