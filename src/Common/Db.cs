using Npgsql; namespace Common; public static class Db{ public static NpgsqlDataSource DataSource(string cs)=>NpgsqlDataSource.Create(cs); }
