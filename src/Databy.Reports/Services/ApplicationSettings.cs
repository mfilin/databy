using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Databy.Reports.Services;

public sealed class ApplicationSettings : IApplicationSettings
{
    public const string ConnectionVariable = "DATABY_CONNECTION";
    public const string ConnectionVariablePrefix = ConnectionVariable + "_";

    private const string SettingsFileName = "appsettings.local.json";

    private static readonly Regex ConnectionNamePattern = new(
        @"^[a-zA-Z][a-zA-Z0-9_-]*$", RegexOptions.CultureInvariant);

    private readonly string _baseDirectory;
    private readonly Lazy<ConnectionConfiguration> _configuration;

    public ApplicationSettings(string? baseDirectory = null)
    {
        _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;
        CatalogRoot = ResolveCatalogRoot(_baseDirectory);
        LogDirectory = Path.Combine(_baseDirectory, "logs");
        _configuration = new Lazy<ConnectionConfiguration>(
            LoadConnections, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public string ConnectionString
    {
        get
        {
            var configuration = _configuration.Value;
            if (configuration.DefaultName.Length == 0)
                throw new InvalidOperationException(NoDefaultConnectionMessage);
            return configuration.Connections[configuration.DefaultName].ConnectionString;
        }
    }

    public IReadOnlyDictionary<string, ConnectionSettings> Connections =>
        _configuration.Value.Connections;

    public string DefaultConnectionName => _configuration.Value.DefaultName;

    public ConnectionSettings GetConnection(string? name)
    {
        var configuration = _configuration.Value;
        if (string.IsNullOrWhiteSpace(name))
        {
            if (configuration.DefaultName.Length == 0)
                throw new InvalidOperationException(NoDefaultConnectionMessage);
            return configuration.Connections[configuration.DefaultName];
        }

        var requested = name.Trim();
        if (configuration.Connections.TryGetValue(requested, out var connection))
            return connection;

        var known = configuration.Connections.Count == 0
            ? "нет"
            : string.Join(", ", configuration.Connections.Keys);
        throw new InvalidOperationException(
            $"Подключение '{requested}' не настроено. Известные подключения: {known}. " +
            $"Опишите его в секции Connections файла {SettingsFile} или задайте строку в переменной " +
            $"{ConnectionVariablePrefix}{NormalizeName(requested)}.");
    }

    public string CatalogRoot { get; }
    public string LogDirectory { get; }

    private string SettingsFile => Path.Combine(_baseDirectory, SettingsFileName);

    private string NoDefaultConnectionMessage =>
        $"Не задано подключение по умолчанию. Укажите DefaultConnection или добавьте подключение main " +
        $"в файле {SettingsFile}, либо задайте строку в переменной {ConnectionVariable}.";

    private ConnectionConfiguration LoadConnections()
    {
        var file = SettingsFile;
        var connections = new Dictionary<string, ConnectionSettings>(StringComparer.OrdinalIgnoreCase);
        string? legacyConnectionString = null;
        var requestedDefault = "";

        if (File.Exists(file))
        {
            var text = ReadSettingsFile(file);
            using var document = ParseJson(file, text);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw FormatError(file, "корневой элемент должен быть JSON-объектом");

            if (root.TryGetProperty("ConnectionString", out var legacy))
                legacyConnectionString = ReadString(legacy, "ConnectionString", file);

            if (root.TryGetProperty("DefaultConnection", out var defaultElement))
                requestedDefault = ReadString(defaultElement, "DefaultConnection", file)?.Trim() ?? "";

            if (root.TryGetProperty("Connections", out var section))
            {
                if (section.ValueKind != JsonValueKind.Object)
                    throw FormatError(file, "секция Connections должна быть объектом");
                foreach (var property in section.EnumerateObject())
                    AddConnection(connections, property.Name, property.Value, file);
            }
        }

        // Старая настройка описывает подключение main типа postgres; новое main имеет приоритет.
        if (!string.IsNullOrWhiteSpace(legacyConnectionString) && !connections.ContainsKey("main"))
        {
            connections["main"] = new ConnectionSettings(
                "main", ConnectionTypes.Postgres, legacyConnectionString.Trim());
        }

        var defaultName = ResolveDefaultName(connections, requestedDefault, file);

        var environmentConnectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.IsNullOrWhiteSpace(environmentConnectionString))
        {
            if (defaultName.Length > 0)
            {
                var current = connections[defaultName];
                connections[defaultName] = new ConnectionSettings(
                    current.Name, current.Type, environmentConnectionString.Trim());
            }
            else
            {
                connections["main"] = new ConnectionSettings(
                    "main", ConnectionTypes.Postgres, environmentConnectionString.Trim());
                defaultName = "main";
            }
        }

        ApplyNamedEnvironmentOverrides(connections, file);

        return new ConnectionConfiguration(
            new ReadOnlyDictionary<string, ConnectionSettings>(connections), defaultName);
    }

    private static string ResolveDefaultName(
        Dictionary<string, ConnectionSettings> connections, string requested, string file)
    {
        if (requested.Length > 0)
        {
            if (!ConnectionNamePattern.IsMatch(requested))
            {
                throw FormatError(
                    file, $"недопустимое имя подключения по умолчанию '{requested}'");
            }

            foreach (var name in connections.Keys)
            {
                if (name.Equals(requested, StringComparison.OrdinalIgnoreCase)) return name;
            }

            throw FormatError(file,
                $"подключение по умолчанию '{requested}' не описано в секции Connections " +
                $"(известные: {DescribeNames(connections)})");
        }

        if (connections.ContainsKey("main")) return "main";
        if (connections.Count == 1) return connections.Keys.First();
        return "";
    }

    private static void ApplyNamedEnvironmentOverrides(
        Dictionary<string, ConnectionSettings> connections, string file)
    {
        var byVariable = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in connections.Keys)
        {
            var suffix = NormalizeName(name);
            if (byVariable.TryGetValue(suffix, out var other))
            {
                throw FormatError(file,
                    $"имена подключений '{other}' и '{name}' используют одну переменную " +
                    $"{ConnectionVariablePrefix}{suffix}");
            }

            byVariable.Add(suffix, name);
        }

        foreach (var (suffix, name) in byVariable)
        {
            var value = Environment.GetEnvironmentVariable(ConnectionVariablePrefix + suffix);
            if (string.IsNullOrWhiteSpace(value)) continue;

            var connection = connections[name];
            connections[name] = new ConnectionSettings(
                connection.Name, connection.Type, value.Trim());
        }
    }

    private static void AddConnection(
        Dictionary<string, ConnectionSettings> connections,
        string name,
        JsonElement element,
        string file)
    {
        if (!ConnectionNamePattern.IsMatch(name))
            throw FormatError(file, $"недопустимое имя подключения '{name}'");

        if (connections.ContainsKey(name))
        {
            throw FormatError(file,
                $"повторяется имя подключения '{name}' (сравнение без учёта регистра)");
        }

        if (element.ValueKind != JsonValueKind.Object)
            throw FormatError(file, $"подключение '{name}' должно быть объектом");

        var type = element.TryGetProperty("Type", out var typeElement)
            ? ReadString(typeElement, $"Connections.{name}.Type", file)
            : null;
        if (string.IsNullOrWhiteSpace(type))
            throw FormatError(file, $"для подключения '{name}' не задан Type");

        var normalizedType = type.Trim().ToLowerInvariant();
        if (!ConnectionTypes.All.Contains(normalizedType))
        {
            throw FormatError(file,
                $"недопустимый Type '{type.Trim()}' для подключения '{name}'. " +
                $"Допустимые типы: {string.Join(", ", ConnectionTypes.All)}");
        }

        var connectionString = element.TryGetProperty("ConnectionString", out var valueElement)
            ? ReadString(valueElement, $"Connections.{name}.ConnectionString", file)
            : null;
        if (string.IsNullOrWhiteSpace(connectionString))
            throw FormatError(file, $"для подключения '{name}' не задана строка подключения");

        connections.Add(name, new ConnectionSettings(
            name, normalizedType, connectionString.Trim()));
    }

    private static string ReadSettingsFile(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"Не удалось прочитать файл настроек {file}: {exception.Message}");
        }
    }

    private static JsonDocument ParseJson(string file, string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            throw FormatError(file, $"ошибка разбора JSON: {exception.Message}");
        }
    }

    private static string? ReadString(JsonElement element, string property, string file)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.String)
            throw FormatError(file, $"свойство {property} должно быть строкой");
        return element.GetString();
    }

    private static string DescribeNames(
        IReadOnlyDictionary<string, ConnectionSettings> connections) =>
        connections.Count == 0 ? "нет" : string.Join(", ", connections.Keys);

    private static string NormalizeName(string name)
    {
        var builder = new char[name.Length];
        for (var i = 0; i < name.Length; i++)
        {
            var symbol = name[i];
            builder[i] = char.IsLetterOrDigit(symbol) ? char.ToUpperInvariant(symbol) : '_';
        }

        return new string(builder);
    }

    private static InvalidOperationException FormatError(string file, string message) =>
        new($"Некорректный файл настроек {file}: {message}");

    private static string ResolveCatalogRoot(string baseDirectory)
    {
        var direct = Path.Combine(baseDirectory, "catalog");
        if (Directory.Exists(direct) || string.Equals(Path.GetFullPath(baseDirectory), Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase))
            return direct;
        for (var directory = new DirectoryInfo(baseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "catalog");
            if (Directory.Exists(candidate)) return candidate;
        }
        return direct;
    }

    private sealed record ConnectionConfiguration(
        IReadOnlyDictionary<string, ConnectionSettings> Connections, string DefaultName);
}
