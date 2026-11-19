using Npgsql;
using Pure.Collections.Generic;
using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.HashCodes;
using Pure.RelationalSchema.Storage.Abstractions;
using Pure.RelationalSchema.Storage.PostgreSQL.Abstractions;
using Pure.RelationalSchema.Storage.PostgreSQL.Insertion;
using Pure.RelationalSchema.Storage.PostgreSQL.Upserting.Tests.Fakes;
using String = Pure.Primitives.String.String;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting.Tests;

public sealed record PostgreSqlStoredSchemaDataSetWithUpsertedRowsTests
    : IAsyncLifetime,
        IDisposable
{
    private DatabaseFixture? _fixture;

    [Fact]
    public void UpdatesMatchedRowAndInsertsUnmatchedOne()
    {
        SeedUsers();

        Touch(
            Upserting(
                _fixture!.Users,
                new Upsert(
                    [_fixture.Id],
                    User("1", "Alicia", "31", "false"),
                    User("4", "Dave", "50", "true")
                )
            )
        );

        Assert.Equal(
            [
                "1|Alicia|31|false",
                "2|Bob|40|true",
                "3|Carol|30|false",
                "4|Dave|50|true",
            ],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void InsertsEveryRowIntoEmptyTable()
    {
        Touch(
            Upserting(
                _fixture!.Users,
                new Upsert(
                    [_fixture.Id],
                    User("1", "Alice", "30", "true"),
                    User("2", "Bob", "40", "false")
                )
            )
        );

        Assert.Equal(
            ["1|Alice|30|true", "2|Bob|40|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void MatchesCompositeKey()
    {
        SeedUsers();

        Touch(
            Upserting(
                _fixture!.Users,
                new Upsert(
                    [_fixture.Name, _fixture.Age],
                    User("6", "Carol", "30", "true"),
                    User("5", "Carol", "31", "true"),
                    User("8", "Bob", "30", "false")
                )
            )
        );

        Assert.Equal(
            [
                "1|Alice|30|true",
                "2|Bob|40|true",
                "5|Carol|31|true",
                "6|Carol|30|true",
                "8|Bob|30|false",
            ],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void KeyColumnOrderDoesNotMatter()
    {
        SeedUsers();

        Touch(
            Upserting(
                _fixture!.Users,
                new Upsert(
                    [_fixture.Active, _fixture.Age, _fixture.Id],
                    User("3", "Caroline", "30", "false"),
                    User("4", "Dave", "20", "true")
                )
            )
        );

        Assert.Equal(
            [
                "1|Alice|30|true",
                "2|Bob|40|true",
                "3|Caroline|30|false",
                "4|Dave|20|true",
            ],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void KeyOfEveryColumnKeepsMatchedRowsAndInsertsOthers()
    {
        _fixture!.Seed(_fixture.Orders, ["1", "first"], ["2", "second"]);

        Touch(
            Upserting(
                _fixture.Orders,
                new Upsert(
                    [_fixture.Note, _fixture.OrderId],
                    Order("1", "first"),
                    Order("2", "2nd"),
                    Order("3", "third")
                )
            )
        );

        Assert.Equal(
            ["1|first", "2|2nd", "2|second", "3|third"],
            _fixture.Stored(_fixture.Orders)
        );
    }

    [Fact]
    public void UpdatesEveryStoredRowMatchingKey()
    {
        _fixture!.Seed(_fixture.Orders, ["1", "a"], ["1", "b"], ["2", "c"]);

        Touch(
            Upserting(_fixture.Orders, new Upsert([_fixture.OrderId], Order("1", "z")))
        );

        Assert.Equal(["1|z", "1|z", "2|c"], _fixture.Stored(_fixture.Orders));
    }

    [Fact]
    public void LeavesRowsOutsideOfBatchUntouched()
    {
        SeedUsers();

        Touch(
            Upserting(
                _fixture!.Users,
                new Upsert([_fixture.Id], User("2", "Robert", "41", "false"))
            )
        );

        Assert.Equal(
            ["1|Alice|30|true", "2|Robert|41|false", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void EmptyKeyThrowsAndLeavesTableUntouched()
    {
        SeedUsers();
        IPostgreSqlStoredSchemaDataSet dataSet = Upserting(
            _fixture!.Users,
            new Upsert([], User("1", "Everyone", "0", "false"))
        );

        _ = Assert.Throws<ArgumentException>(() => Touch(dataSet));

        Assert.Equal(
            ["1|Alice|30|true", "2|Bob|40|true", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void EmptyKeyOfGroupingWithoutRowsThrows()
    {
        IPostgreSqlStoredSchemaDataSet dataSet = Upserting(
            _fixture!.Users,
            new Upsert([_fixture.Id], User("1", "Changed", "30", "true")),
            new Upsert([])
        );

        _ = Assert.Throws<ArgumentException>(() => Touch(dataSet));
    }

    [Fact]
    public void KeyColumnOutsideOfTableInGroupingWithoutRowsThrows()
    {
        IPostgreSqlStoredSchemaDataSet dataSet = Upserting(
            _fixture!.Users,
            new Upsert([_fixture.Note])
        );

        _ = Assert.Throws<ArgumentException>(() => Touch(dataSet));
    }

    [Fact]
    public void KeyColumnOutsideOfTableThrowsAndLeavesTableUntouched()
    {
        SeedUsers();
        IPostgreSqlStoredSchemaDataSet dataSet = Upserting(
            _fixture!.Users,
            new Upsert([_fixture.Id], User("1", "Changed", "30", "true")),
            new Upsert([_fixture.Note], User("2", "Robert", "40", "true"))
        );

        _ = Assert.Throws<ArgumentException>(() => Touch(dataSet));

        Assert.Equal(
            ["1|Alice|30|true", "2|Bob|40|true", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void SameKeyTwiceInBatchFailsAndRollsBackTable()
    {
        SeedUsers();
        IPostgreSqlStoredSchemaDataSet dataSet = Upserting(
            _fixture!.Users,
            new Upsert([_fixture.Id], User("3", "Caroline", "30", "false")),
            new Upsert(
                [_fixture.Id],
                User("1", "Alicia", "30", "true"),
                User("1", "Alice B.", "30", "true")
            )
        );

        PostgresException exception = Assert.Throws<PostgresException>(() =>
            Touch(dataSet)
        );

        Assert.Equal(PostgresErrorCodes.CardinalityViolation, exception.SqlState);
        Assert.Equal(
            ["1|Alice|30|true", "2|Bob|40|true", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void SameNewKeyTwiceInBatchIsInsertedTwice()
    {
        Touch(
            Upserting(
                _fixture!.Orders,
                new Upsert([_fixture.OrderId], Order("1", "a"), Order("1", "b"))
            )
        );

        Assert.Equal(["1|a", "1|b"], _fixture.Stored(_fixture.Orders));
    }

    [Fact]
    public void UniqueIndexViolationFailsAndRollsBackTable()
    {
        SeedUsers();
        IPostgreSqlStoredSchemaDataSet dataSet = Upserting(
            _fixture!.Users,
            new Upsert([_fixture.Id], User("1", "Changed", "30", "true")),
            new Upsert([_fixture.Name], User("2", "Bobby", "40", "true"))
        );

        PostgresException exception = Assert.Throws<PostgresException>(() =>
            Touch(dataSet)
        );

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal(
            ["1|Alice|30|true", "2|Bob|40|true", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void RowWithFewerCellsFailsAndRollsBackTable()
    {
        SeedUsers();
        IPostgreSqlStoredSchemaDataSet dataSet = Upserting(
            _fixture!.Users,
            new Upsert(
                [_fixture.Id],
                User("1", "Changed", "30", "true"),
                Row(
                    Cell(_fixture.Id, "2"),
                    Cell(_fixture.Name, "Robert"),
                    Cell(_fixture.Active, "true")
                )
            )
        );

        PostgresException exception = Assert.Throws<PostgresException>(() =>
            Touch(dataSet)
        );

        Assert.Equal(PostgresErrorCodes.SyntaxError, exception.SqlState);
        Assert.Equal(
            ["1|Alice|30|true", "2|Bob|40|true", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void ColumnWithoutCellsIsKeptOnUpdate()
    {
        SeedUsers();

        Touch(
            Upserting(
                _fixture!.Users,
                new Upsert(
                    [_fixture.Id],
                    Row(Cell(_fixture.Id, "1"), Cell(_fixture.Name, "Alicia"))
                )
            )
        );

        Assert.Equal(
            ["1|Alicia|30|true", "2|Bob|40|true", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void ColumnWithoutCellsFailsOnInsert()
    {
        SeedUsers();
        IPostgreSqlStoredSchemaDataSet dataSet = Upserting(
            _fixture!.Users,
            new Upsert(
                [_fixture.Id],
                Row(Cell(_fixture.Id, "1"), Cell(_fixture.Name, "Alicia")),
                Row(Cell(_fixture.Id, "9"), Cell(_fixture.Name, "Ivan"))
            )
        );

        PostgresException exception = Assert.Throws<PostgresException>(() =>
            Touch(dataSet)
        );

        Assert.Equal(PostgresErrorCodes.NotNullViolation, exception.SqlState);
        Assert.Equal(
            ["1|Alice|30|true", "2|Bob|40|true", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void CellOutsideOfTableFailsAndRollsBackTable()
    {
        SeedUsers();
        IPostgreSqlStoredSchemaDataSet dataSet = Upserting(
            _fixture!.Users,
            new Upsert([_fixture.Id], User("1", "Changed", "30", "true")),
            new Upsert(
                [_fixture.Id],
                Row(
                    Cell(_fixture.Id, "2"),
                    Cell(_fixture.Name, "Robert"),
                    Cell(_fixture.Age, "40"),
                    Cell(_fixture.Active, "true"),
                    Cell(_fixture.Note, "Order note")
                )
            )
        );

        PostgresException exception = Assert.Throws<PostgresException>(() =>
            Touch(dataSet)
        );

        Assert.Equal(PostgresErrorCodes.UndefinedColumn, exception.SqlState);
        Assert.Equal(
            ["1|Alice|30|true", "2|Bob|40|true", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void MatchesAndWritesValuesWithSingleQuotes()
    {
        _fixture!.Seed(_fixture.Orders, ["1", "it's"], ["2", "its"]);

        Touch(
            Upserting(
                _fixture.Orders,
                new Upsert(
                    [_fixture.Note],
                    Order("10", "it's"),
                    Order("3", "O'Reilly's ''quoted''")
                )
            )
        );

        Assert.Equal(
            ["10|it's", "2|its", "3|O'Reilly's ''quoted''"],
            _fixture.Stored(_fixture.Orders)
        );
    }

    [Fact]
    public void MatchesAndWritesValuesWithBackslashes()
    {
        IPostgreSqlStoredSchemaDataSet stored = Stored();
        Touch(
            new PostgreSqlStoredSchemaDataSetWithInsertedRows(
                stored,
                [
                    new TableRows(
                        _fixture!.Orders,
                        [Order("1", @"C:\temp\n"), Order("2", "plain")]
                    ),
                ]
            )
        );

        Touch(
            Upserting(
                _fixture.Orders,
                new Upsert([_fixture.Note], Order("5", @"C:\temp\n"), Order("6", @"D:\"))
            )
        );

        Assert.Equal(
            ["2|plain", @"5|C:\temp\n", @"6|D:\"],
            _fixture.Stored(_fixture.Orders)
        );
    }

    [Fact]
    public void RunsGroupingsOfTableInSuppliedOrder()
    {
        SeedUsers();

        Touch(
            Upserting(
                _fixture!.Users,
                new Upsert([_fixture.Id], User("1", "Moved", "30", "true")),
                new Upsert([_fixture.Name], User("7", "Moved", "70", "false")),
                new Upsert([_fixture.Id], User("1", "Lost", "30", "true"))
            )
        );

        Assert.Equal(
            [
                "1|Lost|30|true",
                "2|Bob|40|true",
                "3|Carol|30|false",
                "7|Moved|70|false",
            ],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void SkipsGroupingWithoutRows()
    {
        SeedUsers();

        Touch(
            Upserting(
                _fixture!.Users,
                new Upsert([_fixture.Id]),
                new Upsert([_fixture.Id], User("2", "Robert", "40", "true"))
            )
        );

        Assert.Equal(
            ["1|Alice|30|true", "2|Robert|40|true", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void ExecutesNothingWithoutRows()
    {
        SeedUsers();
        IPostgreSqlStoredSchemaDataSet dataSet = Upserting(
            _fixture!.Users,
            new Upsert([_fixture.Id]),
            new Upsert([_fixture.Name, _fixture.Id])
        );

        Touch(dataSet);

        Assert.Equal(
            ["1|Alice|30|true", "2|Bob|40|true", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void ExecutesNothingWithoutGroupings()
    {
        SeedUsers();

        Touch(Upserting(_fixture!.Users));

        Assert.Equal(
            ["1|Alice|30|true", "2|Bob|40|true", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void DoesNotUpsertBeforeFirstAccess()
    {
        SeedUsers();

        _ = Upserting(
            _fixture!.Users,
            new Upsert([_fixture.Id], User("1", "Alicia", "30", "true"))
        );

        Assert.Equal(
            ["1|Alice|30|true", "2|Bob|40|true", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void DoesNotCheckKeyBeforeFirstAccess()
    {
        SeedUsers();

        IPostgreSqlStoredSchemaDataSet dataSet = Upserting(
            _fixture!.Users,
            new Upsert([], User("1", "Alicia", "30", "true"))
        );

        _ = Assert.Single(dataSet);
    }

    [Fact]
    public void UpsertsOnlyOnce()
    {
        IPostgreSqlStoredSchemaDataSet dataSet = Upserting(
            _fixture!.Orders,
            new Upsert([_fixture.OrderId], Order("1", "upserted"))
        );

        _ = dataSet[_fixture.Orders].TableSchema;
        _fixture.Seed(_fixture.Orders, ["1", "seeded"]);
        _ = dataSet[_fixture.Orders].ElementType;
        _ = dataSet[_fixture.Orders].AsEnumerable().Count();

        Assert.Equal(["1|seeded", "1|upserted"], _fixture.Stored(_fixture.Orders));
    }

    [Fact]
    public void UpsertsEverySuppliedTable()
    {
        SeedUsers();
        _fixture!.Seed(_fixture.Orders, ["1", "first"], ["2", "second"]);

        Touch(
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                Stored(),
                [
                    new TableUpserts(
                        _fixture.Orders,
                        new Upsert([_fixture.OrderId], Order("2", "2nd"))
                    ),
                    new TableUpserts(
                        _fixture.Users,
                        new Upsert([_fixture.Id], User("2", "Robert", "40", "true"))
                    ),
                ]
            )
        );

        Assert.Equal(["1|first", "2|2nd"], _fixture.Stored(_fixture.Orders));
        Assert.Equal(
            ["1|Alice|30|true", "2|Robert|40|true", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void UpsertsNothingInTablesOutsideOfGroupings()
    {
        SeedUsers();
        _fixture!.Seed(_fixture.Orders, ["1", "first"]);

        Touch(
            Upserting(
                _fixture.Users,
                new Upsert([_fixture.Id], User("1", "Alicia", "30", "true"))
            )
        );

        Assert.Equal(["1|first"], _fixture.Stored(_fixture.Orders));
        Assert.Equal(
            ["1|Alicia|30|true", "2|Bob|40|true", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    [Fact]
    public void FailingTableDoesNotRollBackOtherTables()
    {
        _fixture!.Seed(_fixture.Orders, ["1", "first"]);
        IPostgreSqlStoredSchemaDataSet dataSet =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                Stored(),
                [
                    new TableUpserts(
                        _fixture.Orders,
                        new Upsert([_fixture.OrderId], Order("1", "1st"))
                    ),
                    new TableUpserts(
                        _fixture.Users,
                        new Upsert(
                            [_fixture.Id],
                            User("1", "Alice", "30", "true"),
                            User("1", "Alice", "30", "true")
                        )
                    ),
                ]
            );

        _ = dataSet[_fixture.Orders].TableSchema;
        _ = Assert.Throws<PostgresException>(() => dataSet[_fixture.Users].TableSchema);

        Assert.Equal(["1|1st"], _fixture.Stored(_fixture.Orders));
        Assert.Empty(_fixture.Stored(_fixture.Users));
    }

    [Fact]
    public void WrappingTwiceAppliesInnerUpsertsFirst()
    {
        SeedUsers();

        Touch(
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                Upserting(
                    _fixture!.Users,
                    new Upsert([_fixture.Id], User("1", "Inner", "30", "true"))
                ),
                [
                    new TableUpserts(
                        _fixture.Users,
                        new Upsert([_fixture.Name], User("1", "Inner", "31", "true"))
                    ),
                ]
            )
        );

        Assert.Equal(
            ["1|Inner|31|true", "2|Bob|40|true", "3|Carol|30|false"],
            _fixture.Stored(_fixture.Users)
        );
    }

    public void Dispose()
    {
        _fixture?.Dispose();
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public Task InitializeAsync()
    {
        _fixture = new DatabaseFixture();
        return Task.CompletedTask;
    }

    private static void Touch(IPostgreSqlStoredSchemaDataSet dataSet)
    {
        foreach (IStoredTableDataSet tableDataSet in dataSet.Values)
        {
            _ = tableDataSet.TableSchema;
        }
    }

    private static KeyValuePair<IColumn, ICell> Cell(IColumn column, string value)
    {
        return new KeyValuePair<IColumn, ICell>(column, new Cell(new String(value)));
    }

    private static IRow Row(params IEnumerable<KeyValuePair<IColumn, ICell>> cells)
    {
        return new Row(
            new Dictionary<KeyValuePair<IColumn, ICell>, IColumn, ICell>(
                cells,
                x => x.Key,
                x => x.Value,
                x => new ColumnHash(x)
            )
        );
    }

    private void SeedUsers()
    {
        _fixture!.Seed(
            _fixture.Users,
            ["1", "Alice", "30", "true"],
            ["2", "Bob", "40", "true"],
            ["3", "Carol", "30", "false"]
        );
    }

    private IRow User(string id, string name, string age, string active)
    {
        return Row(
            Cell(_fixture!.Id, id),
            Cell(_fixture.Name, name),
            Cell(_fixture.Age, age),
            Cell(_fixture.Active, active)
        );
    }

    private IRow Order(string id, string note)
    {
        return Row(Cell(_fixture!.OrderId, id), Cell(_fixture.Note, note));
    }

    private PostgreSqlStoredSchemaDataSetWithUpsertedRows Upserting(
        ITable table,
        params IEnumerable<IGrouping<IEnumerable<IColumn>, IRow>> upserts
    )
    {
        return new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
            Stored(),
            [new TableUpserts(table, upserts)]
        );
    }

    private FakeStoredSchemaDataSet Stored()
    {
        return new FakeStoredSchemaDataSet(
            _fixture!.Schema,
            _fixture.Connection,
            _ => []
        );
    }
}
