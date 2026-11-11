using System.Globalization;
using System.Text.Json;
using MySqlConnector;

internal static class Program
{
    private const string BaselineMigrationId = "20261105083211_InitialSchemaBaseline";
    private const string EfProductVersion = "9.0.0";
    private const string HistoryTable = "__EFMigrationsHistory";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var connectionString = Environment.GetEnvironmentVariable("SMES_MIGRATIONS_CONNECTION_STRING");
            if (string.IsNullOrWhiteSpace(connectionString))
                return Fail("Set SMES_MIGRATIONS_CONNECTION_STRING through a secret-safe environment source.");

            var options = new MySqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(options.Database))
                return Fail("The connection string must name one database explicitly.");

            var allowE2e = args.Contains("--allow-e2e", StringComparer.Ordinal);
            var capturePath = GetOption(args, "--capture-contract");
            var contractPath = GetOption(args, "--contract") ?? "scripts/migrations/baseline-schema-contract.json";
            var apply = args.Contains("--apply", StringComparer.Ordinal);
            if (args.Any(x => x.StartsWith("--", StringComparison.Ordinal)
                && x is not "--allow-e2e" and not "--apply" and not "--confirm-smes-v2-baseline"
                    and not "--verify" and not "--contract" and not "--capture-contract"))
                return Fail("Unknown argument. See docs/DATABASE_MIGRATIONS.md for supported commands.");

            if (capturePath is not null && (!allowE2e || !options.Database.EndsWith("_e2e", StringComparison.OrdinalIgnoreCase)))
                return Fail("Contract capture is read-only and restricted to an explicitly allowed *_e2e database.");

            if (apply)
            {
                var approvedSmesV2 = string.Equals(options.Database, "smes_v2", StringComparison.OrdinalIgnoreCase)
                    && args.Contains("--confirm-smes-v2-baseline", StringComparer.Ordinal);
                var approvedE2e = allowE2e && options.Database.EndsWith("_e2e", StringComparison.OrdinalIgnoreCase);
                if (!approvedSmesV2 && !approvedE2e)
                    return Fail("Applying the history marker requires exact database smes_v2 plus --confirm-smes-v2-baseline, or a *_e2e database plus --allow-e2e.");
            }

            await using var connection = new MySqlConnection(connectionString);
            await connection.OpenAsync();
            var selectedDatabase = Convert.ToString(await new MySqlCommand("SELECT DATABASE()", connection).ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            if (!string.Equals(selectedDatabase, options.Database, StringComparison.OrdinalIgnoreCase))
                return Fail($"Connected database '{selectedDatabase}' does not match the connection string's named database.");

            var actual = await ReadContractAsync(connection);
            if (capturePath is not null)
            {
                if (!actual.Tables.Any(x => x.Name == HistoryTable))
                    return Fail("Contract capture requires an already migrated fresh *_e2e database with EF history.");
                await using var historyCount = new MySqlCommand($"SELECT COUNT(*) FROM `{HistoryTable}`", connection);
                var migrationCount = Convert.ToInt64(await historyCount.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
                await using var baselineCount = new MySqlCommand(
                    $"SELECT COUNT(*) FROM `{HistoryTable}` WHERE `MigrationId` = @migrationId AND `ProductVersion` = @productVersion",
                    connection);
                baselineCount.Parameters.AddWithValue("@migrationId", BaselineMigrationId);
                baselineCount.Parameters.AddWithValue("@productVersion", EfProductVersion);
                var matchingBaselineRows = Convert.ToInt64(await baselineCount.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
                if (migrationCount != 1 || matchingBaselineRows != 1)
                    return Fail("Contract capture is restricted to a fresh database containing only the expected baseline migration.");

                actual = actual with { MigrationId = BaselineMigrationId, ProductVersion = EfProductVersion };
                var fullPath = Path.GetFullPath(capturePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, JsonSerializer.Serialize(actual, JsonOptions));
                Console.WriteLine($"Captured read-only schema contract from {selectedDatabase}: {actual.Tables.Count} tables, {actual.Columns.Count} columns, {actual.Indexes.Count} index entries, {actual.Constraints.Count} constraints.");
                Console.WriteLine(fullPath);
                return 0;
            }

            var expected = JsonSerializer.Deserialize<SchemaContract>(await File.ReadAllTextAsync(contractPath), JsonOptions);
            if (expected is null || expected.MigrationId != BaselineMigrationId || expected.ProductVersion != EfProductVersion)
                return Fail("The contract is missing or does not describe this baseline migration and EF product version.");

            var expectedApplicationSchema = WithoutHistoryTable(expected, includeHistory: false);
            var actualApplicationSchema = WithoutHistoryTable(actual, includeHistory: false);
            if (!CompareContracts("application schema", expectedApplicationSchema, actualApplicationSchema))
                return Fail("Schema preflight failed; no EF migration history was created or changed.");

            var historyExists = actual.Tables.Any(x => x.Name == HistoryTable);
            var existingRows = 0L;
            if (historyExists)
            {
                var expectedHistory = WithoutHistoryTable(expected, includeHistory: true, historyOnly: true);
                var actualHistory = WithoutHistoryTable(actual, includeHistory: true, historyOnly: true);
                if (!CompareContracts("existing EF history table", expectedHistory, actualHistory))
                    return Fail("Existing EF history table has an unexpected shape; no rows were changed.");

                await using var countCommand = new MySqlCommand($"SELECT COUNT(*) FROM `{HistoryTable}`", connection);
                existingRows = Convert.ToInt64(await countCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
                if (apply && existingRows != 0)
                    return Fail("EF migration history is already populated; baseline adoption is not applicable.");
            }

            Console.WriteLine($"PASS schema contract: {actualApplicationSchema.Tables.Count} tables, {actualApplicationSchema.Columns.Count} columns, {actualApplicationSchema.Indexes.Count} index entries, {actualApplicationSchema.Constraints.Count} constraints.");
            if (!apply)
            {
                if (existingRows > 0)
                {
                    await using var baselineCommand = new MySqlCommand(
                        $"SELECT COUNT(*) FROM `{HistoryTable}` WHERE `MigrationId` = @migrationId AND `ProductVersion` = @productVersion",
                        connection);
                    baselineCommand.Parameters.AddWithValue("@migrationId", BaselineMigrationId);
                    baselineCommand.Parameters.AddWithValue("@productVersion", EfProductVersion);
                    var baselineRows = Convert.ToInt64(await baselineCommand.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
                    if (baselineRows != 1)
                        return Fail("History is populated but does not contain exactly the expected baseline marker.");
                    Console.WriteLine($"Baseline marker already present and verified: {BaselineMigrationId} (EF {EfProductVersion}).");
                }
                Console.WriteLine("Verify-only mode: database was not changed.");
                return 0;
            }

            if (!historyExists)
            {
                await using var createHistory = new MySqlCommand(
                    "CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (" +
                    "`MigrationId` varchar(150) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL, " +
                    "`ProductVersion` varchar(32) CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci NOT NULL, " +
                    "CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci",
                    connection);
                await createHistory.ExecuteNonQueryAsync();
            }

            var afterCreate = await ReadContractAsync(connection);
            var expectedHistoryAfterCreate = WithoutHistoryTable(expected, includeHistory: true, historyOnly: true);
            var actualHistoryAfterCreate = WithoutHistoryTable(afterCreate, includeHistory: true, historyOnly: true);
            if (!CompareContracts("EF history table after creation", expectedHistoryAfterCreate, actualHistoryAfterCreate))
                return Fail("EF history table verification failed; no migration row was inserted.");

            await using (var insert = new MySqlCommand(
                $"INSERT INTO `{HistoryTable}` (`MigrationId`, `ProductVersion`) VALUES (@migrationId, @productVersion)", connection))
            {
                insert.Parameters.AddWithValue("@migrationId", BaselineMigrationId);
                insert.Parameters.AddWithValue("@productVersion", EfProductVersion);
                await insert.ExecuteNonQueryAsync();
            }

            await using var readback = new MySqlCommand(
                $"SELECT COUNT(*) FROM `{HistoryTable}` WHERE `MigrationId` = @migrationId AND `ProductVersion` = @productVersion",
                connection);
            readback.Parameters.AddWithValue("@migrationId", BaselineMigrationId);
            readback.Parameters.AddWithValue("@productVersion", EfProductVersion);
            var insertedRows = Convert.ToInt64(await readback.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            if (insertedRows != 1)
                return Fail("Baseline history insert read-back did not return exactly one matching row.");

            Console.WriteLine($"Baseline marker inserted and read back: {BaselineMigrationId} (EF {EfProductVersion}).");
            return 0;
        }
        catch (Exception exception)
        {
            return Fail($"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static string? GetOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine($"REFUSED: {message}");
        return 2;
    }

    private static SchemaContract WithoutHistoryTable(SchemaContract source, bool includeHistory, bool historyOnly = false)
    {
        bool Keep(string name) => historyOnly ? name == HistoryTable : includeHistory || name != HistoryTable;
        return source with
        {
            Tables = source.Tables.Where(x => Keep(x.Name)).ToList(),
            Columns = source.Columns.Where(x => Keep(x.TableName)).ToList(),
            Indexes = source.Indexes.Where(x => Keep(x.TableName)).ToList(),
            Constraints = source.Constraints.Where(x => Keep(x.TableName)).ToList()
        };
    }

    private static bool CompareContracts(string label, SchemaContract expected, SchemaContract actual)
    {
        var matches = true;
        matches &= CompareRows(label + ".tables", expected.Tables, actual.Tables);
        matches &= CompareRows(label + ".columns", expected.Columns, actual.Columns);
        matches &= CompareRows(label + ".indexes", expected.Indexes, actual.Indexes);
        matches &= CompareRows(label + ".constraints", expected.Constraints, actual.Constraints);
        return matches;
    }

    private static bool CompareRows<T>(string label, IReadOnlyCollection<T> expected, IReadOnlyCollection<T> actual) where T : notnull
    {
        var expectedSet = expected.ToHashSet();
        var actualSet = actual.ToHashSet();
        var missing = expectedSet.Except(actualSet).ToList();
        var extra = actualSet.Except(expectedSet).ToList();
        if (missing.Count == 0 && extra.Count == 0 && expected.Count == actual.Count)
            return true;

        Console.Error.WriteLine($"MISMATCH {label}: expected {expected.Count}, actual {actual.Count}, missing {missing.Count}, extra {extra.Count}.");
        foreach (var row in missing.Take(8)) Console.Error.WriteLine($"  missing: {row}");
        foreach (var row in extra.Take(8)) Console.Error.WriteLine($"  extra:   {row}");
        return false;
    }

    private static async Task<SchemaContract> ReadContractAsync(MySqlConnection connection)
    {
        var tables = await ReadRowsAsync(connection,
            "SELECT TABLE_NAME,TABLE_TYPE,COALESCE(ENGINE,''),COALESCE(TABLE_COLLATION,'') " +
            "FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() ORDER BY TABLE_NAME",
            reader => new TableShape(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));

        var columns = await ReadRowsAsync(connection,
            "SELECT TABLE_NAME,COLUMN_NAME,COLUMN_TYPE,IS_NULLABLE,COLUMN_DEFAULT,EXTRA," +
            "COALESCE(CHARACTER_SET_NAME,''),COALESCE(COLLATION_NAME,''),ORDINAL_POSITION " +
            "FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() ORDER BY TABLE_NAME,ORDINAL_POSITION",
            reader => new ColumnShape(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                ReadNullable(reader, 4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetInt32(8)));

        var indexes = await ReadRowsAsync(connection,
            "SELECT TABLE_NAME,INDEX_NAME,NON_UNIQUE,SEQ_IN_INDEX,COLUMN_NAME,SUB_PART,INDEX_TYPE " +
            "FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=DATABASE() ORDER BY TABLE_NAME,INDEX_NAME,SEQ_IN_INDEX",
            reader => new IndexShape(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3),
                ReadNullable(reader, 4), ReadNullable(reader, 5), reader.GetString(6)));

        var constraints = await ReadRowsAsync(connection,
            "SELECT tc.TABLE_NAME,tc.CONSTRAINT_NAME,tc.CONSTRAINT_TYPE,COALESCE(kcu.COLUMN_NAME,'')," +
            "COALESCE(kcu.REFERENCED_TABLE_NAME,''),COALESCE(kcu.REFERENCED_COLUMN_NAME,'')," +
            "COALESCE(rc.UPDATE_RULE,''),COALESCE(rc.DELETE_RULE,''),COALESCE(kcu.ORDINAL_POSITION,0) " +
            "FROM information_schema.TABLE_CONSTRAINTS tc " +
            "LEFT JOIN information_schema.KEY_COLUMN_USAGE kcu ON tc.CONSTRAINT_SCHEMA=kcu.CONSTRAINT_SCHEMA " +
            "AND tc.TABLE_NAME=kcu.TABLE_NAME AND tc.CONSTRAINT_NAME=kcu.CONSTRAINT_NAME " +
            "LEFT JOIN information_schema.REFERENTIAL_CONSTRAINTS rc ON tc.CONSTRAINT_SCHEMA=rc.CONSTRAINT_SCHEMA " +
            "AND tc.TABLE_NAME=rc.TABLE_NAME AND tc.CONSTRAINT_NAME=rc.CONSTRAINT_NAME " +
            "WHERE tc.CONSTRAINT_SCHEMA=DATABASE() ORDER BY tc.TABLE_NAME,tc.CONSTRAINT_NAME,kcu.ORDINAL_POSITION",
            reader => new ConstraintShape(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetInt32(8)));

        return new SchemaContract(BaselineMigrationId, EfProductVersion, tables, columns, indexes, constraints);
    }

    private static async Task<List<T>> ReadRowsAsync<T>(MySqlConnection connection, string sql, Func<MySqlDataReader, T> map)
    {
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<T>();
        while (await reader.ReadAsync()) rows.Add(map(reader));
        return rows;
    }

    private static string? ReadNullable(MySqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture);

    private sealed record TableShape(string Name, string Type, string Engine, string Collation);
    private sealed record ColumnShape(string TableName, string Name, string Type, string Nullable, string? Default,
        string Extra, string CharacterSet, string Collation, int Ordinal);
    private sealed record IndexShape(string TableName, string Name, int NonUnique, int Sequence, string? Column,
        string? PrefixLength, string Type);
    private sealed record ConstraintShape(string TableName, string Name, string Type, string Column,
        string ReferencedTable, string ReferencedColumn, string UpdateRule, string DeleteRule, int Ordinal);
    private sealed record SchemaContract(string MigrationId, string ProductVersion, List<TableShape> Tables,
        List<ColumnShape> Columns, List<IndexShape> Indexes, List<ConstraintShape> Constraints);
}
