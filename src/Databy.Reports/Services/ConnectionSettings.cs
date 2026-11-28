namespace Databy.Reports.Services;

public static class ConnectionTypes
{
    public const string Postgres = "postgres";
    public const string ClickHouse = "clickhouse";

    public static readonly IReadOnlyList<string> All = [Postgres, ClickHouse];
}

/// <summary>
/// Именованное подключение. ToString() намеренно не выводит строку подключения:
/// она не должна попадать в логи, сообщения и отладочный вывод.
/// </summary>
public sealed class ConnectionSettings
{
    public ConnectionSettings(string name, string type, string connectionString)
    {
        Name = name;
        Type = type;
        ConnectionString = connectionString;
    }

    public string Name { get; }
    public string Type { get; }
    public string ConnectionString { get; }

    public override string ToString() => $"{Name} ({Type})";
}
