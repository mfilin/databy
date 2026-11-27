using System.Security.Cryptography;
using System.Text;
using Databy.Reports.Catalog;

namespace Tests.Unit;

[Trait("Category", "T1")]
public sealed class SqlFileLoaderTests
{
    [Fact]
    public void Sf01_LoadsRelativeFileAndComputesSha256()
    {
        using var dir = new TempDirectory();
        var bytes = Encoding.UTF8.GetBytes("SELECT 1");
        CatalogTests.Write(dir.Path, "sql/q.sql", "SELECT 1");

        var script = SqlFileLoader.Load(dir.Path, "sql/q.sql");

        Assert.Equal("SELECT 1", script.Text);
        Assert.Equal("sql/q.sql", script.RelativePath);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), script.Hash);
    }

    [Theory]
    [InlineData("absolute", "относительным")]
    [InlineData("../outside.sql", "..")]
    [InlineData("sql/../../outside.sql", "..")]
    [InlineData("sql/q.txt", "расширение")]
    [InlineData("sql/a:b.sql", "':'")]
    public void Sf02_RejectsPathsOutsideRules(string relativePath, string expected)
    {
        using var dir = new TempDirectory();
        var actual = relativePath == "absolute"
            ? Path.Combine(dir.Path, "q.sql")
            : relativePath;

        var exception = Assert.Throws<SqlFileException>(() => SqlFileLoader.Load(dir.Path, actual));
        Assert.Contains(expected, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sf03_RejectsMissingFile()
    {
        using var dir = new TempDirectory();
        var exception = Assert.Throws<SqlFileException>(() => SqlFileLoader.Load(dir.Path, "sql/missing.sql"));
        Assert.Contains("не найден", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sf04_RejectsFileLargerThan256Kb()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "sql/big.sql", new string('a', SqlFileLoader.MaximumBytes + 1));

        var exception = Assert.Throws<SqlFileException>(() => SqlFileLoader.Load(dir.Path, "sql/big.sql"));
        Assert.Contains("превышает", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sf05_RejectsInvalidUtf8()
    {
        using var dir = new TempDirectory();
        var path = Path.Combine(dir.Path, "bad.sql");
        Directory.CreateDirectory(dir.Path);
        File.WriteAllBytes(path, [0x53, 0x45, 0x4C, 0x45, 0x43, 0x54, 0x20, 0xFF]);

        var exception = Assert.Throws<SqlFileException>(() => SqlFileLoader.Load(dir.Path, "bad.sql"));
        Assert.Contains("UTF-8", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sf06_AcceptsBomButDoesNotPutItIntoText()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.Path);
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("SELECT 1")).ToArray();
        File.WriteAllBytes(Path.Combine(dir.Path, "bom.sql"), bytes);

        var script = SqlFileLoader.Load(dir.Path, "bom.sql");

        Assert.Equal("SELECT 1", script.Text);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), script.Hash);
    }

    [Fact]
    public void Sf07_LoadsEmptyFileForLaterValidation()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "empty.sql", "");

        var script = SqlFileLoader.Load(dir.Path, "empty.sql");

        Assert.Equal("", script.Text);
    }

    [Fact]
    public void Sf08_NormalizesBackslashInRelativePath()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "sql/q.sql", "SELECT 1");

        var script = SqlFileLoader.Load(dir.Path, @"sql\q.sql");

        Assert.Equal("sql/q.sql", script.RelativePath);
    }
}

[Trait("Category", "T1")]
public sealed class SqlTokenizerTests
{
    [Fact]
    public void St01_IgnoresStringsQuotedIdentifiersAndComments()
    {
        var tokens = SqlTokenizer.Tokenize(
            "SELECT '; DROP', \"FORMAT\", `x` -- DROP\n # SETTINGS\n FROM t /* ; INSERT */");

        Assert.Contains(tokens, token => token.Kind == SqlTokenKind.String && token.Text == "; DROP");
        Assert.Contains(tokens, token => token.Kind == SqlTokenKind.QuotedIdentifier && token.Text == "FORMAT");
        Assert.Contains(tokens, token => token.Kind == SqlTokenKind.QuotedIdentifier && token.Text == "x");
        Assert.DoesNotContain(tokens, token => token.Kind == SqlTokenKind.Word && token.Text == "DROP");
        Assert.DoesNotContain(tokens, token => token.Kind == SqlTokenKind.Punctuation && token.Text == ";");
    }

    [Fact]
    public void St02_ExtractsPlaceholdersWithNestedTypes()
    {
        var tokens = SqlTokenizer.Tokenize(
            "SELECT {p_from:Date}, {p_sum:Decimal(18, 2)}, {p_arr:Array(String)}");

        var placeholders = tokens.Where(token => token.Kind == SqlTokenKind.Placeholder).ToArray();

        Assert.Equal(3, placeholders.Length);
        Assert.Equal("p_from", placeholders[0].Name);
        Assert.Equal("Date", placeholders[0].TypeText);
        Assert.Equal("Decimal(18, 2)", placeholders[1].TypeText);
        Assert.Equal("Array(String)", placeholders[2].TypeText);
    }

    [Theory]
    [InlineData("SELECT {p:}")]
    [InlineData("SELECT {p:Date")]
    public void St03_ProducesInvalidPlaceholderToken(string sql)
    {
        var tokens = SqlTokenizer.Tokenize(sql);
        Assert.Contains(tokens, token => token.Kind == SqlTokenKind.InvalidPlaceholder);
    }

    [Fact]
    public void St04_DoesNotTreatMapLiteralAsPlaceholder()
    {
        var tokens = SqlTokenizer.Tokenize("SELECT {'a':1}");

        Assert.DoesNotContain(tokens, token => token.Kind == SqlTokenKind.Placeholder);
        Assert.Contains(tokens, token => token.Kind == SqlTokenKind.Punctuation && token.Text == "{");
    }

    [Fact]
    public void St05_SupportsHeredocStrings()
    {
        var tokens = SqlTokenizer.Tokenize("SELECT $tag$ ; DROP $tag$");

        Assert.Contains(tokens, token => token.Kind == SqlTokenKind.String && token.Text.Contains("; DROP"));
        Assert.DoesNotContain(tokens, token => token.Kind == SqlTokenKind.Punctuation && token.Text == ";");
    }

    [Theory]
    [InlineData("SELECT 'abc")]
    [InlineData("SELECT \"abc")]
    [InlineData("SELECT /* abc")]
    public void St06_RejectsUnterminatedConstructs(string sql)
    {
        Assert.Throws<SqlSyntaxException>(() => SqlTokenizer.Tokenize(sql));
    }
}

[Trait("Category", "T1")]
public sealed class SqlValidatorTests
{
    [Fact]
    public void Sv01_AcceptsSelectAndWith()
    {
        var select = Validate(
            "SELECT {p_from:Date}, {p_sum:Decimal(18,2)} FROM t WHERE d = {p_from:Date}",
            Param("p_from", "date"), Param("p_sum", "decimal"));

        Assert.Empty(select);
        Assert.Empty(Validate("WITH x AS (SELECT 1 AS a) SELECT * FROM x"));
    }

    [Theory]
    [InlineData("INSERT INTO t SELECT 1")]
    [InlineData("CREATE TABLE t (x Int32)")]
    [InlineData("DROP TABLE t")]
    [InlineData("SET max_threads = 1")]
    [InlineData("SYSTEM FLUSH LOGS")]
    [InlineData("USE db")]
    [InlineData("EXPLAIN SELECT 1")]
    public void Sv02_RejectsNotSelectOrWith(string sql)
    {
        var issues = Validate(sql);
        Assert.NotEmpty(issues);
    }

    [Theory]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1;;")]
    [InlineData("SELECT 1; DROP TABLE t")]
    public void Sv03_RejectsMoreThanOneStatement(string sql)
    {
        var issues = Validate(sql);
        Assert.Contains(issues, issue => issue.Message.Contains("оператор", StringComparison.OrdinalIgnoreCase)
                                         || issue.Message.Contains("';'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sv04_AllowsTrailingSemicolonAndComments()
    {
        Assert.Empty(Validate("SELECT 1;"));
        Assert.Empty(Validate("SELECT 1; -- конец"));
        Assert.Empty(Validate("SELECT 1; /* конец */"));
    }

    [Theory]
    [InlineData("SELECT 1 FORMAT CSV", "FORMAT")]
    [InlineData("SELECT 1 SETTINGS max_threads = 1", "SETTINGS")]
    [InlineData("SELECT * FROM t INTO OUTFILE 'x'", "INTO OUTFILE")]
    public void Sv05_RejectsOutputAndSettingsClauses(string sql, string expected)
    {
        var issues = Validate(sql);
        Assert.Contains(issues, issue => issue.Message.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("SELECT format, settings FROM t")]
    [InlineData("SELECT t.format FROM t")]
    [InlineData("SELECT 1 AS format, 2 AS settings")]
    public void Sv06_AllowsColumnsNamedFormatAndSettings(string sql)
    {
        Assert.Empty(Validate(sql));
    }

    [Theory]
    [InlineData("url")]
    [InlineData("file")]
    [InlineData("s3")]
    [InlineData("remote")]
    [InlineData("remoteSecure")]
    [InlineData("mysql")]
    [InlineData("postgresql")]
    [InlineData("jdbc")]
    [InlineData("odbc")]
    [InlineData("hdfs")]
    [InlineData("input")]
    [InlineData("executable")]
    public void Sv07_RejectsExternalTableFunctions(string function)
    {
        var issues = Validate($"SELECT * FROM {function}('x')");
        Assert.Contains(issues, issue => issue.Message.Contains(function, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sv08_AllowsColumnsNamedLikeFunctionsAndSystemSchema()
    {
        Assert.Empty(Validate("SELECT file, url FROM t"));
        Assert.Empty(Validate("SELECT * FROM system.tables"));
        Assert.Empty(Validate("SELECT system FROM t"));
    }

    [Fact]
    public void Sv09_RejectsDmlAfterWith()
    {
        var insert = Validate("WITH x AS (SELECT 1) INSERT INTO t SELECT * FROM x");
        var drop = Validate("WITH x AS (SELECT 1) DROP TABLE t");

        Assert.Contains(insert, issue => issue.Message.Contains("INSERT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(drop, issue => issue.Message.Contains("DROP", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sv10_PlaceholderSetMustMatchParameters()
    {
        var missing = Validate("SELECT {p_from:Date}", Param("p_from", "date"), Param("p_sum", "decimal"));
        var extra = Validate("SELECT {p_from:Date}, {p_extra:String}",
            Param("p_from", "date"));
        var wrongCase = Validate("SELECT {P_FROM:Date}", Param("p_from", "date"));

        Assert.Contains(missing, issue => issue.Message.Contains("p_sum", StringComparison.Ordinal));
        Assert.Contains(extra, issue => issue.Message.Contains("p_extra", StringComparison.Ordinal));
        Assert.Contains(wrongCase, issue => issue.Message.Contains("P_FROM", StringComparison.Ordinal));
        Assert.Contains(wrongCase, issue => issue.Message.Contains("p_from", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("String", "int", false)]
    [InlineData("Int32", "int", true)]
    [InlineData("Decimal(10,2)", "decimal", true)]
    [InlineData("Date32", "date", true)]
    [InlineData("Nullable(String)", "string", true)]
    public void Sv11_ChecksPlaceholderTypeFamily(string placeholderType, string yamlType, bool accepted)
    {
        var issues = Validate($"SELECT {{p:{placeholderType}}}", Param("p", yamlType));
        Assert.Equal(accepted, issues.Count == 0);
    }

    [Fact]
    public void Sv12_ArrayPlaceholderPointsToListReports()
    {
        var issues = Validate("SELECT {p:Array(String)}", Param("p", "string"));
        Assert.Contains(issues, issue => issue.Message.Contains("input_keys", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sv13_DbTypeConstrainsPlaceholderType()
    {
        Assert.Empty(Validate("SELECT {p:Int32}", Param("p", "int", "Int32")));

        var mismatch = Validate("SELECT {p:Int64}", Param("p", "int", "Int32"));
        Assert.Contains(mismatch, issue => issue.Message.Contains("db_type", StringComparison.OrdinalIgnoreCase));

        var bad = Validate("SELECT {p:Int32}", Param("p", "int", "Array(String)"));
        Assert.Contains(bad, issue => issue.Message.Contains("db_type", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(Validate("SELECT {p:String}", Param("p", "string", "Nullable(String)")));
    }

    [Fact]
    public void Sv14_RejectsDifferentTypesForSamePlaceholder()
    {
        var issues = Validate("SELECT {p:Date}, {p:String}", Param("p", "date"));
        Assert.Contains(issues, issue => issue.Message.Contains("разными типами", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sv15_RejectsReservedInputKeysCteForListReports()
    {
        const string sql = "WITH input_keys AS (SELECT 1 AS row_id) SELECT * FROM input_keys";

        var issues = SqlValidator.Validate(sql, [], hasInput: true);
        Assert.Contains(issues, issue => issue.Message.Contains("input_keys", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(SqlValidator.Validate(sql, [], hasInput: false));

        const string withColumns =
            "WITH input_keys(row_id) AS (SELECT 1 AS row_id) SELECT * FROM input_keys";
        var columnIssues = SqlValidator.Validate(withColumns, [], hasInput: true);
        Assert.Contains(columnIssues,
            issue => issue.Message.Contains("input_keys", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("SELECT (1", "Несбалансированные")]
    [InlineData("SELECT 1)", "Лишняя закрывающая")]
    public void Sv16_RejectsUnbalancedParentheses(string sql, string expected)
    {
        var issues = Validate(sql);
        Assert.Contains(issues, issue => issue.Message.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("")]
    [InlineData(";")]
    [InlineData("   ")]
    public void Sv17_RejectsEmptySql(string sql)
    {
        var issues = Validate(sql);
        Assert.Contains(issues, issue => issue.Message.Contains("пуст", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sv18_RejectsInvalidPlaceholder()
    {
        var issues = Validate("SELECT {p:}", Param("p", "date"));
        Assert.Contains(issues, issue => issue.Message.Contains("Некорректный плейсхолдер", StringComparison.OrdinalIgnoreCase));

        var withoutType = Validate("SELECT {p}", Param("p", "date"));
        Assert.Contains(withoutType,
            issue => issue.Message.Contains("Некорректный плейсхолдер", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(Validate("SELECT {'a': 1} AS m"));
    }

    [Fact]
    public void Sv19_IgnoresForbiddenWordsInStringsAndComments()
    {
        Assert.Empty(Validate("SELECT 'DROP TABLE x', \"INSERT\" FROM t -- DROP"));
        Assert.Empty(Validate("SELECT 1 /* ; DROP TABLE t */"));
    }

    [Fact]
    public void Sv23_RejectsSecondStatementWithoutSemicolon()
    {
        var issues = Validate("SELECT 1 SELECT 2");
        Assert.Contains(issues, issue => issue.Message.Contains("второй оператор", StringComparison.OrdinalIgnoreCase));

        var with = Validate("SELECT 1 WITH x AS (SELECT 1) SELECT * FROM x");
        Assert.Contains(with, issue => issue.Message.Contains("второй оператор", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("SELECT 1 UNION ALL SELECT 2")]
    [InlineData("SELECT 1 EXCEPT SELECT 2")]
    [InlineData("SELECT 1 INTERSECT SELECT 2")]
    [InlineData("SELECT (SELECT 1)")]
    public void Sv24_AllowsSetOperationsAndScalarSubqueries(string sql)
    {
        Assert.Empty(Validate(sql));
    }

    [Fact]
    public void Sv25_RejectsWithWithoutSelect()
    {
        var issues = Validate("WITH 1 AS x");
        Assert.Contains(issues, issue => issue.Message.Contains("После WITH", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sv21_RejectsQuotedExternalTableFunction()
    {
        var issues = Validate("SELECT * FROM \"url\"('x')");
        Assert.Contains(issues, issue => issue.Message.Contains("url", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Sv22_RejectsSettingsInsideSubqueryButAllowsSettingsColumn()
    {
        var clause = Validate("SELECT * FROM (SELECT 1 SETTINGS max_threads = 1)");
        var column = Validate("SELECT * FROM (SELECT settings FROM t)");

        Assert.Contains(clause, issue => issue.Message.Contains("SETTINGS", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(column);
    }

    private static IReadOnlyList<SqlValidationIssue> Validate(
        string sql, params ParameterDefinition[] parameters) =>
        SqlValidator.Validate(sql, parameters, hasInput: false);

    private static ParameterDefinition Param(string name, string type, string? dbType = null) => new()
    {
        Name = name,
        Type = type,
        DbType = dbType,
        Required = true
    };
}

[Trait("Category", "T1")]
public sealed class SqlCatalogTests
{
    [Fact]
    public void Sc01_LoadsQueryReportAndStoresScript()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "sql/q.sql", "SELECT {p_from:Date}");
        CatalogTests.Write(dir.Path, "reports/q.yml", QueryYaml("q.report", "sql/q.sql", ParameterYaml("p_from", "date")));

        var result = new ReportCatalogLoader().Load(dir.Path);

        Assert.Empty(result.Errors);
        var report = Assert.Single(result.Reports);
        Assert.NotNull(report.Script);
        Assert.Equal("SELECT {p_from:Date}", report.Script!.Text);
        Assert.Equal(64, report.Script.Hash.Length);
    }

    [Fact]
    public void Sc02_MissingSqlFileIsCatalogError()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "reports/q.yml", QueryYaml("q.report", "sql/missing.sql", ""));

        var result = new ReportCatalogLoader().Load(dir.Path);

        Assert.Empty(result.Reports);
        Assert.Contains("SQL-файл", Assert.Single(result.Errors).Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("не найден", result.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sc03_PlaceholderMismatchIsCatalogError()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "sql/q.sql", "SELECT 1");
        CatalogTests.Write(dir.Path, "reports/q.yml", QueryYaml("q.report", "sql/q.sql", ParameterYaml("p_from", "date")));

        var result = new ReportCatalogLoader().Load(dir.Path);

        Assert.Empty(result.Reports);
        Assert.Contains("p_from", Assert.Single(result.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Sc04_FunctionWithFileIsRejected()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "sql/q.sql", "SELECT 1");
        CatalogTests.Write(dir.Path, "reports/q.yml", """
            code: q.report
            title: Отчёт
            routine:
              name: reports.fn_test
              kind: function
              file: sql/q.sql
            roles: [admin, user]
            """);

        var result = new ReportCatalogLoader().Load(dir.Path);

        Assert.Empty(result.Reports);
        Assert.Contains("routine.file", Assert.Single(result.Errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sc05_QueryWithAbsoluteFileIsRejected()
    {
        using var dir = new TempDirectory();
        var absolute = Path.Combine(dir.Path, "q.sql").Replace('\\', '/');
        CatalogTests.Write(dir.Path, "reports/q.yml", QueryYaml("q.report", absolute, ""));

        var result = new ReportCatalogLoader().Load(dir.Path);

        Assert.Empty(result.Reports);
        Assert.Contains("относительным", Assert.Single(result.Errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sc06_QueryWithInvalidDbTypeIsRejected()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "sql/q.sql", "SELECT {p:Int32}");
        CatalogTests.Write(dir.Path, "reports/q.yml",
            QueryYaml("q.report", "sql/q.sql", ParameterYaml("p", "int", "String")));

        var result = new ReportCatalogLoader().Load(dir.Path);

        Assert.Empty(result.Reports);
        Assert.Contains("db_type", Assert.Single(result.Errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sc07_QueryWithMatchingDbTypeLoads()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "sql/q.sql", "SELECT {p:Int32}");
        CatalogTests.Write(dir.Path, "reports/q.yml",
            QueryYaml("q.report", "sql/q.sql", ParameterYaml("p", "int", "Int32")));

        var result = new ReportCatalogLoader().Load(dir.Path);

        Assert.Empty(result.Errors);
        Assert.Single(result.Reports);
    }

    [Fact]
    public void Sc08_OtherReportsSurviveSqlError()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "good.yml", CatalogTests.Valid("good"));
        CatalogTests.Write(dir.Path, "reports/q.yml", QueryYaml("q.report", "sql/missing.sql", ""));

        var result = new ReportCatalogLoader().Load(dir.Path);

        Assert.Equal("good", Assert.Single(result.Reports).Code);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Sc09_SqlPathIsRelativeToCatalogRoot()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "sql/q.sql", "SELECT 1");
        CatalogTests.Write(dir.Path, "folder/q.yml", QueryYaml("q.report", "sql/q.sql", ""));

        var result = new ReportCatalogLoader().Load(dir.Path);

        Assert.Empty(result.Errors);
        Assert.Single(result.Reports);
    }

    [Fact]
    public void Sc10_EmptySourceIsRejected()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "sql/q.sql", "SELECT 1");
        CatalogTests.Write(dir.Path, "reports/q.yml", QueryYaml("q.report", "sql/q.sql", "", "''"));

        var result = new ReportCatalogLoader().Load(dir.Path);

        Assert.Empty(result.Reports);
        Assert.Contains("source", Assert.Single(result.Errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sc11_EmptySqlFileIsCatalogError()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "sql/q.sql", "");
        CatalogTests.Write(dir.Path, "reports/q.yml", QueryYaml("q.report", "sql/q.sql", ""));

        var result = new ReportCatalogLoader().Load(dir.Path);

        Assert.Empty(result.Reports);
        Assert.Contains("пуст", Assert.Single(result.Errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sc12_ReservedInputKeysCteIsRejectedForListReport()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "sql/q.sql",
            "WITH input_keys AS (SELECT 1 AS row_id) SELECT * FROM input_keys");
        CatalogTests.Write(dir.Path, "reports/q.yml", """
            code: q.list
            title: Список
            source: ch
            routine:
              kind: query
              file: sql/q.sql
            input:
              row_id_parameter: p_row_id
              result_key: row_id
              columns:
                - { name: p_inn, type: string }
            roles: [admin, user]
            """);

        var result = new ReportCatalogLoader().Load(dir.Path);

        Assert.Empty(result.Reports);
        Assert.Contains("input_keys", Assert.Single(result.Errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Sc13_FunctionWithDbTypeIsRejected()
    {
        using var dir = new TempDirectory();
        CatalogTests.Write(dir.Path, "q.yml", """
            code: q.fn
            title: Отчёт
            routine:
              name: reports.fn_test
              kind: function
            parameters:
              - { name: p, type: int, db_type: Int32 }
            roles: [admin, user]
            """);

        var result = new ReportCatalogLoader().Load(dir.Path);

        Assert.Empty(result.Reports);
        Assert.Contains("db_type", Assert.Single(result.Errors).Message, StringComparison.OrdinalIgnoreCase);
    }

    private static string QueryYaml(string code, string file, string parameterYaml, string source = "ch")
    {
        var parameters = string.IsNullOrWhiteSpace(parameterYaml) ? "parameters: []" : parameterYaml;
        return $"code: {code}\n" +
               "title: Отчёт\n" +
               $"source: {source}\n" +
               "routine:\n" +
               "  kind: query\n" +
               $"  file: '{file}'\n" +
               $"{parameters}\n" +
               "roles: [admin, user]\n";
    }

    private static string ParameterYaml(string name, string type, string? dbType = null) =>
        dbType is null
            ? $"parameters:\n  - name: {name}\n    type: {type}\n    label: Параметр"
            : $"parameters:\n  - name: {name}\n    type: {type}\n    db_type: {dbType}\n    label: Параметр";
}