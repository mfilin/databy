using System.Text;
using Databy.Reports.Services;

namespace Tests.Unit;

[Trait("Category", "T1")]
[Collection("settings-env")]
public sealed class ConnectionSettingsTests
{
    [Fact]
    public void Cn01_LegacyFilePropertyBecomesMainPostgres()
    {
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, null));
        WriteSettings(dir.Path, """{"ConnectionString":"Host=legacy;Database=reports"}""");

        var settings = new ApplicationSettings(dir.Path);

        Assert.Equal("main", settings.DefaultConnectionName);
        var main = Assert.Single(settings.Connections);
        Assert.Equal("main", main.Key);
        Assert.Equal(ConnectionTypes.Postgres, main.Value.Type);
        Assert.Equal("Host=legacy;Database=reports", main.Value.ConnectionString);
        Assert.Same(main.Value, settings.GetConnection(null));
        Assert.Equal("Host=legacy;Database=reports", settings.ConnectionString);
    }

    [Fact]
    public void Cn01_LegacyEnvironmentVariableBecomesMainPostgres()
    {
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, "Host=legacy-env;Database=reports"));

        var settings = new ApplicationSettings(dir.Path);

        Assert.Equal("main", settings.DefaultConnectionName);
        var main = Assert.Single(settings.Connections);
        Assert.Equal(ConnectionTypes.Postgres, main.Value.Type);
        Assert.Equal("Host=legacy-env;Database=reports", main.Value.ConnectionString);
        Assert.Equal("Host=legacy-env;Database=reports", settings.ConnectionString);
        Assert.Equal("Host=legacy-env;Database=reports", settings.GetConnection(null).ConnectionString);
    }

    [Fact]
    public void Cn02_NamedConnectionsAreResolvedCaseInsensitively()
    {
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, null),
            ("DATABY_CONNECTION_MAIN", null),
            ("DATABY_CONNECTION_CH", null));
        WriteSettings(dir.Path, """
            {
              "Connections": {
                "main": { "Type": "postgres", "ConnectionString": "Host=main-db;Database=reports" },
                "ch":   { "Type": "clickhouse", "ConnectionString": "Host=ch-db;Port=8123;Protocol=http" }
              },
              "DefaultConnection": "ch"
            }
            """);

        var settings = new ApplicationSettings(dir.Path);

        Assert.Equal(2, settings.Connections.Count);
        Assert.True(settings.Connections.ContainsKey("MAIN"));
        Assert.Equal(ConnectionTypes.Postgres, settings.GetConnection("MAIN").Type);
        Assert.Equal("Host=main-db;Database=reports", settings.GetConnection("Main").ConnectionString);

        var defaultConnection = settings.GetConnection(null);
        Assert.Equal("ch", defaultConnection.Name);
        Assert.Equal(ConnectionTypes.ClickHouse, defaultConnection.Type);
        Assert.Equal("Host=ch-db;Port=8123;Protocol=http", defaultConnection.ConnectionString);
        Assert.Equal("ch", settings.DefaultConnectionName);
        Assert.Equal(defaultConnection.ConnectionString, settings.ConnectionString);
    }

    [Fact]
    public void Cn03_DefaultConnectionWinsOverMain()
    {
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, null));
        WriteSettings(dir.Path, """
            {
              "Connections": {
                "main": { "Type": "postgres", "ConnectionString": "Host=main" },
                "other": { "Type": "postgres", "ConnectionString": "Host=other" }
              },
              "DefaultConnection": "OTHER"
            }
            """);

        var settings = new ApplicationSettings(dir.Path);

        Assert.Equal("other", settings.DefaultConnectionName);
        Assert.Equal("other", settings.GetConnection(null).Name);
        Assert.Equal("Host=other", settings.ConnectionString);
    }

    [Fact]
    public void Cn03_MainBecomesDefaultWhenDefaultConnectionIsNotSet()
    {
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, null));
        WriteSettings(dir.Path, """
            {
              "Connections": {
                "main": { "Type": "postgres", "ConnectionString": "Host=main" },
                "other": { "Type": "postgres", "ConnectionString": "Host=other" }
              }
            }
            """);

        var settings = new ApplicationSettings(dir.Path);

        Assert.Equal("main", settings.DefaultConnectionName);
        Assert.Equal("Host=main", settings.GetConnection(null).ConnectionString);
    }

    [Fact]
    public void Cn03_SingleConnectionBecomesDefault()
    {
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, null));
        WriteSettings(dir.Path, """
            {
              "Connections": {
                "solo": { "Type": "clickhouse", "ConnectionString": "Host=solo" }
              }
            }
            """);

        var settings = new ApplicationSettings(dir.Path);

        Assert.Equal("solo", settings.DefaultConnectionName);
        Assert.Equal("solo", settings.GetConnection(null).Name);
    }

    [Fact]
    public void Cn03_AmbiguousOrMissingDefaultProducesClearError()
    {
        using var ambiguous = new TempDirectory();
        using var empty = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, null));

        WriteSettings(ambiguous.Path, """
            {
              "Connections": {
                "a": { "Type": "postgres", "ConnectionString": "Host=a" },
                "b": { "Type": "postgres", "ConnectionString": "Host=b" }
              }
            }
            """);
        var ambiguousSettings = new ApplicationSettings(ambiguous.Path);

        Assert.Equal("", ambiguousSettings.DefaultConnectionName);
        var exception = Assert.Throws<InvalidOperationException>(
            () => ambiguousSettings.GetConnection(null));
        Assert.Contains("по умолчанию", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ApplicationSettings.ConnectionVariable, exception.Message);
        Assert.Contains("appsettings.local.json", exception.Message);

        var emptySettings = new ApplicationSettings(empty.Path);
        Assert.Equal("", emptySettings.DefaultConnectionName);
        Assert.Throws<InvalidOperationException>(() => emptySettings.ConnectionString);
    }

    [Fact]
    public void Cn04_NamedVariableBeatsGenericAndGenericAppliesToDefault()
    {
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, "Host=generic"),
            ("DATABY_CONNECTION_CH", "Host=named-ch"),
            ("DATABY_CONNECTION_MAIN", null),
            ("DATABY_CONNECTION_GHOST", "Host=ghost"));
        WriteSettings(dir.Path, """
            {
              "Connections": {
                "main": { "Type": "postgres", "ConnectionString": "Host=file-main" },
                "ch":   { "Type": "clickhouse", "ConnectionString": "Host=file-ch" }
              },
              "DefaultConnection": "ch"
            }
            """);

        var settings = new ApplicationSettings(dir.Path);

        Assert.Equal("Host=named-ch", settings.GetConnection("ch").ConnectionString);
        Assert.Equal(ConnectionTypes.ClickHouse, settings.GetConnection("ch").Type);
        Assert.Equal("Host=file-main", settings.GetConnection("main").ConnectionString);
        Assert.False(settings.Connections.ContainsKey("ghost"));
    }

    [Fact]
    public void Cn04_GenericVariableOverridesDefaultWhenNoNamedVariable()
    {
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, "Host=generic"),
            ("DATABY_CONNECTION_CH", null));
        WriteSettings(dir.Path, """
            {
              "Connections": {
                "main": { "Type": "postgres", "ConnectionString": "Host=file-main" },
                "ch":   { "Type": "clickhouse", "ConnectionString": "Host=file-ch" }
              },
              "DefaultConnection": "ch"
            }
            """);

        var settings = new ApplicationSettings(dir.Path);

        Assert.Equal("Host=generic", settings.GetConnection("ch").ConnectionString);
        Assert.Equal("Host=file-main", settings.GetConnection("main").ConnectionString);
    }

    [Fact]
    public void Cn04_GenericVariableCreatesMainWhenNothingIsConfigured()
    {
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, "Host=env-only"));

        var settings = new ApplicationSettings(dir.Path);

        Assert.Equal("main", settings.DefaultConnectionName);
        var connection = Assert.Single(settings.Connections).Value;
        Assert.Equal("main", connection.Name);
        Assert.Equal(ConnectionTypes.Postgres, connection.Type);
        Assert.Equal("Host=env-only", connection.ConnectionString);
    }

    [Fact]
    public void Cn04_HyphenatedNameUsesNormalizedVariable()
    {
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, null),
            ("DATABY_CONNECTION_CH_MAIN", "Host=hyphen"));
        WriteSettings(dir.Path, """
            {
              "Connections": {
                "main": { "Type": "postgres", "ConnectionString": "Host=file-main" },
                "ch-main": { "Type": "clickhouse", "ConnectionString": "Host=file-ch" }
              }
            }
            """);

        var settings = new ApplicationSettings(dir.Path);

        Assert.Equal("Host=hyphen", settings.GetConnection("ch-main").ConnectionString);
        Assert.Equal(ConnectionTypes.ClickHouse, settings.GetConnection("ch-main").Type);
    }

    [Fact]
    public void Cn04_NormalizedVariableConflictIsReported()
    {
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, null));
        WriteSettings(dir.Path, """
            {
              "Connections": {
                "a-b": { "Type": "postgres", "ConnectionString": "Host=a" },
                "a_b": { "Type": "postgres", "ConnectionString": "Host=b" }
              }
            }
            """);

        var settings = new ApplicationSettings(dir.Path);

        var exception = Assert.Throws<InvalidOperationException>(() => settings.Connections);
        Assert.Contains("a-b", exception.Message);
        Assert.Contains("a_b", exception.Message);
        Assert.Contains("DATABY_CONNECTION_A_B", exception.Message);
    }

    [Fact]
    public void Cn05_UnknownConnectionListsKnownNamesWithoutSecrets()
    {
        const string secret = "Password=super-secret";
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, null));
        WriteSettings(dir.Path, $$"""
            {
              "Connections": {
                "main": { "Type": "postgres", "ConnectionString": "Host=db;{{secret}}" }
              }
            }
            """);
        var settings = new ApplicationSettings(dir.Path);

        var exception = Assert.Throws<InvalidOperationException>(
            () => settings.GetConnection("ghost"));

        Assert.Contains("ghost", exception.Message);
        Assert.Contains("не настроено", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("main", exception.Message);
        Assert.Contains("DATABY_CONNECTION_GHOST", exception.Message);
        Assert.DoesNotContain(secret, exception.Message);
        Assert.DoesNotContain("Host=db", exception.Message);
    }

    [Fact]
    public void Cn05_InvalidTypeShowsAllowedTypesWithoutSecrets()
    {
        const string secret = "Password=super-secret";
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, null));
        WriteSettings(dir.Path, $$"""
            {
              "Connections": {
                "main": { "Type": "oracle", "ConnectionString": "Host=db;{{secret}}" }
              }
            }
            """);
        var settings = new ApplicationSettings(dir.Path);

        var exception = Assert.Throws<InvalidOperationException>(() => settings.Connections);

        Assert.Contains("oracle", exception.Message);
        Assert.Contains("postgres", exception.Message);
        Assert.Contains("clickhouse", exception.Message);
        Assert.DoesNotContain(secret, exception.Message);
    }

    [Theory]
    [InlineData("""{"Connections":{"main":{"Type":"postgres","ConnectionString":"   "}}}""", "строка подключения")]
    [InlineData("""{"Connections":{"main":{"Type":"postgres"}}}""", "строка подключения")]
    [InlineData("""{"Connections":{"main":{"ConnectionString":"Host=x"}}}""", "Type")]
    public void Cn05_IncompleteConnectionIsRejected(string json, string expected)
    {
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, null));
        WriteSettings(dir.Path, json);
        var settings = new ApplicationSettings(dir.Path);

        var exception = Assert.Throws<InvalidOperationException>(() => settings.Connections);

        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("appsettings.local.json", exception.Message);
    }

    [Fact]
    public void Cn05_DuplicateNamesAreRejected()
    {
        const string secret = "Password=super-secret";
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, null));
        WriteSettings(dir.Path, $$"""
            {
              "Connections": {
                "main": { "Type": "postgres", "ConnectionString": "Host=db;{{secret}}" },
                "MAIN": { "Type": "postgres", "ConnectionString": "Host=db2;{{secret}}" }
              }
            }
            """);
        var settings = new ApplicationSettings(dir.Path);

        var exception = Assert.Throws<InvalidOperationException>(() => settings.Connections);

        Assert.Contains("повторяется", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, exception.Message);
    }

    [Fact]
    public void Cn05_InvalidNameIsRejected()
    {
        const string secret = "Password=super-secret";
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, null));
        WriteSettings(dir.Path, $$"""
            {
              "Connections": {
                "bad.name": { "Type": "postgres", "ConnectionString": "Host=db;{{secret}}" }
              }
            }
            """);
        var settings = new ApplicationSettings(dir.Path);

        var exception = Assert.Throws<InvalidOperationException>(() => settings.Connections);

        Assert.Contains("bad.name", exception.Message);
        Assert.Contains("недопустимое имя", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(secret, exception.Message);
    }

    [Fact]
    public void Cn06_BrokenJsonIsLazyAndMentionsOnlyPath()
    {
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, null));
        const string content = "{ \"Connections\": { \"main\": { \"Type\": \"postgres\",";
        WriteSettings(dir.Path, content);

        var settings = new ApplicationSettings(dir.Path);

        Assert.NotNull(settings.CatalogRoot);
        Assert.NotNull(settings.LogDirectory);
        var exception = Assert.Throws<InvalidOperationException>(
            () => settings.GetConnection(null));
        Assert.Contains(dir.Path, exception.Message);
        Assert.Contains("appsettings.local.json", exception.Message);
        Assert.DoesNotContain("Connections", exception.Message);
        Assert.DoesNotContain(content, exception.Message);
    }

    [Fact]
    public void Cn06_WrongStructureIsLazyAndMentionsPath()
    {
        using var dir = new TempDirectory();
        using var environment = new EnvironmentScope(
            (ApplicationSettings.ConnectionVariable, null));
        WriteSettings(dir.Path, """{"Connections":[]}""");

        var settings = new ApplicationSettings(dir.Path);

        var exception = Assert.Throws<InvalidOperationException>(() => settings.Connections);
        Assert.Contains(dir.Path, exception.Message);
        Assert.Contains("Connections", exception.Message);
    }

    [Fact]
    public void Cn07_ToStringDoesNotContainConnectionString()
    {
        var connection = new ConnectionSettings(
            "main", ConnectionTypes.Postgres, "Host=db;Username=user;Password=top-secret");

        var text = connection.ToString();

        Assert.Contains("main", text);
        Assert.Contains("postgres", text);
        Assert.DoesNotContain("Host", text);
        Assert.DoesNotContain("top-secret", text);
        Assert.DoesNotContain(";", text);
    }

    private static void WriteSettings(string directory, string json) =>
        File.WriteAllText(
            Path.Combine(directory, "appsettings.local.json"), json, new UTF8Encoding(false));

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = new(StringComparer.Ordinal);

        public EnvironmentScope(params (string Name, string? Value)[] variables)
        {
            // Убираем все DATABY_CONNECTION* из окружения разработчика, чтобы тест
            // зависел только от файла и явно заданных значений.
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                if (entry.Key is not string name) continue;
                if (!name.Equals(ApplicationSettings.ConnectionVariable, StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith(ApplicationSettings.ConnectionVariablePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (_previous.ContainsKey(name)) continue;
                _previous[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, null);
            }

            foreach (var (name, value) in variables)
            {
                if (!_previous.ContainsKey(name))
                    _previous[name] = Environment.GetEnvironmentVariable(name);
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        public void Dispose()
        {
            foreach (var (name, value) in _previous)
                Environment.SetEnvironmentVariable(name, value);
        }
    }
}
