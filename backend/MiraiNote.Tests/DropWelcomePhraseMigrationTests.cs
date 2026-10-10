using Microsoft.EntityFrameworkCore;
using MiraiNote.Data.Context;
using Npgsql;
using Xunit;

namespace MiraiNote.Tests;

public class DropWelcomePhraseMigrationTests
{
    private const string PhraseMigration = "20261009102128_AddWelcomePhrase";

    [Fact]
    public void Migration_DropsTheTable_AndDoesNotRevokeGrants()
    {
        var path = Directory.GetFiles(FindRepoDir("backend/MiraiNote.Data/Migrations"), "*DropWelcomePhrase.cs", SearchOption.TopDirectoryOnly)
            .Single(item => !item.EndsWith(".Designer.cs", StringComparison.Ordinal));
        var text = File.ReadAllText(path);
        Assert.Contains("DROP TABLE IF EXISTS \"WelcomePhrase\"", text, StringComparison.Ordinal);
        Assert.Contains("不必", text, StringComparison.Ordinal);
        Assert.DoesNotContain("REVOKE", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppliesOnEmptyDatabase_AndOnDatabaseThatHadWelcomePhrase()
    {
        var admin = AdminConnectionString();
        await using var conn = new NpgsqlConnection(admin);
        await conn.OpenAsync();
        var emptyDb = "mn_empty_" + Guid.NewGuid().ToString("N");
        var hadDb = "mn_had_" + Guid.NewGuid().ToString("N");
        await ExecuteAsync(conn, $"CREATE DATABASE \"{emptyDb}\"");
        await ExecuteAsync(conn, $"CREATE DATABASE \"{hadDb}\"");
        try
        {
            await MigrateAsync(admin, emptyDb, null);
            Assert.False(await TableExistsAsync(admin, emptyDb));
            await MigrateAsync(admin, emptyDb, null);
            Assert.False(await TableExistsAsync(admin, emptyDb));

            await MigrateAsync(admin, hadDb, PhraseMigration);
            Assert.True(await TableExistsAsync(admin, hadDb));
            Assert.True(await RowCountAsync(admin, hadDb) > 0);
            await MigrateAsync(admin, hadDb, null);
            Assert.False(await TableExistsAsync(admin, hadDb));
        }
        finally
        {
            await ExecuteAsync(conn, $"DROP DATABASE IF EXISTS \"{emptyDb}\" WITH (FORCE)");
            await ExecuteAsync(conn, $"DROP DATABASE IF EXISTS \"{hadDb}\" WITH (FORCE)");
        }
    }

    private static string AdminConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable("ConnectionStrings__MigrationConnection");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var builder = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" };
            return builder.ConnectionString;
        }

        return "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
    }

    private static async Task MigrateAsync(string admin, string database, string? target)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString;
        var options = new DbContextOptionsBuilder<MiraiNoteDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new MiraiNoteDbContext(options);
        await db.Database.MigrateAsync(target);
    }

    private static async Task<bool> TableExistsAsync(string admin, string database)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString;
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1 FROM information_schema.tables
                WHERE table_schema = 'public' AND table_name = 'WelcomePhrase')
            """,
            conn);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<long> RowCountAsync(string admin, string database)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(admin) { Database = database }.ConnectionString;
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM \"WelcomePhrase\"", conn);
        return (long)(await command.ExecuteScalarAsync() ?? 0L);
    }

    private static async Task ExecuteAsync(NpgsqlConnection conn, string sql)
    {
        await using var command = new NpgsqlCommand(sql, conn);
        await command.ExecuteNonQueryAsync();
    }

    private static string FindRepoDir(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(relative);
    }
}
