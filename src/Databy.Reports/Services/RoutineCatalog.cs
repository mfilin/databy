using Databy.Reports.Catalog;
using Npgsql;

namespace Databy.Reports.Services;

/// <summary>Аргумент функции PostgreSQL, известный из pg_proc.</summary>
public sealed record RoutineArgument(string Name, string TypeName, char Mode);

/// <summary>
/// Проверка соответствия описания отчёта и функции в pg_proc.
/// Входными считаются аргументы с режимами i (in), b (inout), v (variadic).
/// </summary>
public static class RoutineCatalog
{
    /// <summary>Читает аргументы функции; ошибка, если функции нет или она перегружена.</summary>
    public static async Task<IReadOnlyList<RoutineArgument>> LoadArgumentsAsync(
        NpgsqlConnection connection, ReportDefinition report, CancellationToken cancellationToken)
    {
        var parts = report.Routine.Name.Split('.');
        await using var command = new NpgsqlCommand("""
            SELECT p.proargnames, p.proargmodes, p.proargtypes::oid[], p.oid::regprocedure::text
            FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
            WHERE n.nspname = @schema AND p.proname = @name AND p.prokind = 'f'
            """, connection);
        command.Parameters.AddWithValue("schema", parts[0]);
        command.Parameters.AddWithValue("name", parts[1]);

        var matches = new List<(string[]? Names, char[]? Modes, uint[] Types)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var names = reader.IsDBNull(0) ? null : (string[])reader.GetValue(0);
                var modes = reader.IsDBNull(1) ? null : (char[])reader.GetValue(1);
                var types = reader.IsDBNull(2) ? [] : (uint[])reader.GetValue(2);
                matches.Add((names, modes, types));
            }
        }

        if (matches.Count == 0)
            throw new InvalidOperationException($"Функция {report.Routine.Name} отсутствует в PostgreSQL");
        if (matches.Count > 1)
            throw new InvalidOperationException($"Функция {report.Routine.Name} перегружена: найдено {matches.Count} вариантов");

        var (argumentNames, argumentModes, argumentTypes) = matches[0];
        var arguments = new List<RoutineArgument>(argumentTypes.Length);
        for (var i = 0; i < argumentTypes.Length; i++)
        {
            var mode = argumentModes is not null && i < argumentModes.Length ? argumentModes[i] : 'i';
            var name = argumentNames is not null && i < argumentNames.Length ? argumentNames[i] : "";
            arguments.Add(new RoutineArgument(name, TypeName(argumentTypes[i]), mode));
        }
        return arguments;
    }

    public static IEnumerable<RoutineArgument> Inputs(IReadOnlyList<RoutineArgument> arguments) =>
        arguments.Where(argument => argument.Mode is 'i' or 'b' or 'v');

    /// <summary>Проверяет наличие и тип входного аргумента; ошибки содержат имя аргумента.</summary>
    public static void RequireInput(ReportDefinition report, IReadOnlyList<RoutineArgument> arguments,
        string name, params string[] allowedTypes)
    {
        var argument = Inputs(arguments).FirstOrDefault(candidate => candidate.Name == name);
        if (argument is null)
            throw new InvalidOperationException(
                $"Функция {report.Routine.Name} не содержит входного аргумента '{name}'");
        if (argument.Name.Length == 0)
            throw new InvalidOperationException(
                $"У функции {report.Routine.Name} входной аргумент без имени на позиции '{name}'");
        if (allowedTypes.Length > 0 && !allowedTypes.Contains(argument.TypeName, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Аргумент '{name}' функции {report.Routine.Name} имеет тип {argument.TypeName}, " +
                $"ожидался: {string.Join("|", allowedTypes)}");
    }

    private static string TypeName(uint oid) => oid switch
    {
        16 => "bool",
        1000 => "bool[]",
        20 or 23 => "int4",
        1007 => "int4[]",
        21 => "int2",
        1005 => "int2[]",
        1016 => "int8[]",
        25 => "text",
        1009 => "text[]",
        1043 => "varchar",
        1015 => "varchar[]",
        1082 => "date",
        1182 => "date[]",
        1700 => "numeric",
        1231 => "numeric[]",
        _ => "oid:" + oid.ToString(System.Globalization.CultureInfo.InvariantCulture)
    };
}
