using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;
using Xunit;

namespace Tests.Integration;

[Trait("Category", "T2")]
public sealed class PostgreSqlReportTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:18")
        .WithDatabase("reports_test")
        .WithUsername("test_user")
        .WithPassword("test_password")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "db", "init.sql"));
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Db01_FunctionReturnsDynamicColumnsAndTypedValues()
    {
        await using var connection = await Open();
        await using var command = new NpgsqlCommand("SELECT * FROM report_rows(@p_after, @p_label)", connection);
        command.Parameters.Add("p_after", NpgsqlDbType.Date).Value = new DateOnly(2026, 1, 1);
        command.Parameters.Add("p_label", NpgsqlDbType.Text).Value = "alpha";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.Equal(new[] { "id", "label", "amount", "happened", "enabled" }, Enumerable.Range(0, reader.FieldCount).Select(reader.GetName));
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0)); Assert.Equal("alpha", reader.GetString(1)); Assert.Equal(12.50m, reader.GetDecimal(2));
        Assert.Equal(new DateOnly(2026, 1, 3), reader.GetFieldValue<DateOnly>(3)); Assert.True(reader.GetBoolean(4));
    }

    [Fact]
    public async Task Db03_NullAndTypedParametersArePreserved()
    {
        await using var connection = await Open();
        await using var command = new NpgsqlCommand("SELECT * FROM report_rows(@p_after, @p_label)", connection);
        command.Parameters.Add("p_after", NpgsqlDbType.Date).Value = new DateOnly(2026, 2, 1);
        command.Parameters.Add("p_label", NpgsqlDbType.Text).Value = "beta";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); Assert.True(reader.IsDBNull(2));
        Assert.Equal(new DateOnly(2026, 2, 4), reader.GetFieldValue<DateOnly>(3));
    }

    [Fact]
    public async Task Db04_SqlLookingParameterIsTreatedAsData()
    {
        await using var connection = await Open();
        await using var command = new NpgsqlCommand("SELECT * FROM report_rows(@p_after, @p_label)", connection);
        command.Parameters.Add("p_after", NpgsqlDbType.Date).Value = new DateOnly(2026, 1, 1);
        command.Parameters.Add("p_label", NpgsqlDbType.Text).Value = "; DROP TABLE items;--";
        await using (var reader = await command.ExecuteReaderAsync()) Assert.False(await reader.ReadAsync());
        await using var verify = new NpgsqlCommand("SELECT count(*) FROM items", connection);
        Assert.Equal(2L, (long)(await verify.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Db05_MissingRoutineIsRejectedByDatabase()
    {
        await using var connection = await Open();
        await using var command = new NpgsqlCommand("SELECT * FROM reports.no_such_function()", connection);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal("42883", ex.SqlState);
    }

    [Fact]
    public async Task Db10_EmptyResultStillHasSchema()
    {
        await using var connection = await Open();
        await using var command = new NpgsqlCommand("SELECT * FROM report_rows(@p_after, @p_label)", connection);
        command.Parameters.Add("p_after", NpgsqlDbType.Date).Value = new DateOnly(2026, 1, 1);
        command.Parameters.Add("p_label", NpgsqlDbType.Text).Value = "missing";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.Equal(5, reader.FieldCount); Assert.False(await reader.ReadAsync());
    }

    [Fact]
    public async Task Db08_DatabaseExceptionIsReturnedAsPostgresException()
    {
        await using var connection = await Open();
        await using var command = new NpgsqlCommand("SELECT fail_report()", connection);
        var ex = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteScalarAsync());
        Assert.Contains("synthetic failure", ex.MessageText);
    }

    [Fact]
    public async Task Db06_CancellationInterruptsLongRunningQuery()
    {
        await using var connection = await Open();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await using var command = new NpgsqlCommand("SELECT pg_sleep(30)", connection);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => command.ExecuteNonQueryAsync(cts.Token));
        Assert.True(connection.FullState.HasFlag(System.Data.ConnectionState.Open));
    }

    [Fact]
    public async Task Db07_CommandTimeoutCancelsLongRunningQuery()
    {
        await using var connection = await Open();
        await using var command = new NpgsqlCommand("SELECT pg_sleep(3)", connection) { CommandTimeout = 1 };
        var ex = await Assert.ThrowsAsync<NpgsqlException>(() => command.ExecuteNonQueryAsync());
        Assert.IsType<TimeoutException>(ex.InnerException);
    }

    private async Task<NpgsqlConnection> Open()
    {
        var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync(); return connection;
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();
}
