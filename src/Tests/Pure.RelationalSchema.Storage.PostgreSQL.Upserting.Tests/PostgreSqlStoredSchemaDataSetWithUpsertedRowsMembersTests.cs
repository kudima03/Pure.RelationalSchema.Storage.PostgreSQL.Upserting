using System.Collections;
using Pure.Collections.Generic;
using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.HashCodes;
using Pure.RelationalSchema.Storage.Abstractions;
using Pure.RelationalSchema.Storage.PostgreSQL.Abstractions;
using Pure.RelationalSchema.Storage.PostgreSQL.Upserting.Tests.Fakes;
using String = Pure.Primitives.String.String;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting.Tests;

public sealed record PostgreSqlStoredSchemaDataSetWithUpsertedRowsMembersTests
    : IAsyncLifetime,
        IDisposable
{
    private const int ExistingRowsCount = 3;

    private DatabaseFixture? _fixture;

    [Fact]
    public void CountEqualsNumberOfSuppliedGroupings()
    {
        IPostgreSqlStoredSchemaDataSet schema = Schema();

        IPostgreSqlStoredSchemaDataSet dataSet =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                schema,
                Upserts(schema.Keys)
            );

        Assert.Equal(schema.Count, dataSet.Count);
    }

    [Fact]
    public void ExposesOnlyTablesFromSuppliedGroupings()
    {
        IPostgreSqlStoredSchemaDataSet schema = Schema();
        ITable included = schema.Keys.First();
        ITable excluded = schema.Keys.Last();

        IPostgreSqlStoredSchemaDataSet dataSet =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                schema,
                Upserts([included])
            );

        _ = Assert.Single(dataSet);
        Assert.Equal([included], dataSet.Keys);
        _ = Assert.Single(dataSet.Values);
        Assert.True(dataSet.ContainsKey(included));
        Assert.False(dataSet.ContainsKey(excluded));
        _ = Assert.ThrowsAny<Exception>(() => dataSet[excluded]);
    }

    [Fact]
    public void KeysAndValuesFollowSuppliedOrder()
    {
        IPostgreSqlStoredSchemaDataSet schema = Schema();

        IPostgreSqlStoredSchemaDataSet dataSet =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                schema,
                Upserts(schema.Keys.Reverse())
            );

        Assert.Equal(schema.Keys.Reverse(), dataSet.Keys);
        Assert.Equal(dataSet.Count, dataSet.Values.Count());
        Assert.All(
            dataSet.Values.Zip(dataSet.Select(x => x.Value)),
            pair => Assert.Same(pair.First, pair.Second)
        );
    }

    [Fact]
    public void TryGetValueFindsSuppliedTable()
    {
        IPostgreSqlStoredSchemaDataSet schema = Schema();
        ITable table = schema.Keys.First();

        IPostgreSqlStoredSchemaDataSet dataSet =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                schema,
                Upserts([table])
            );

        Assert.True(dataSet.TryGetValue(table, out IStoredTableDataSet? value));
        Assert.Same(dataSet[table], value);
    }

    [Fact]
    public void TryGetValueDoesNotFindMissingTable()
    {
        IPostgreSqlStoredSchemaDataSet schema = Schema();

        IPostgreSqlStoredSchemaDataSet dataSet =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                schema,
                Upserts([schema.Keys.First()])
            );

        Assert.False(dataSet.TryGetValue(schema.Keys.Last(), out IStoredTableDataSet? _));
    }

    [Fact]
    public void ExposesConnectionAndSchemaOfWrappedDataSet()
    {
        IPostgreSqlStoredSchemaDataSet schema = Schema();

        IPostgreSqlStoredSchemaDataSet dataSet =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                schema,
                Upserts(schema.Keys)
            );

        Assert.Same(schema.Connection, dataSet.Connection);
        Assert.Same(schema.Schema, dataSet.Schema);
    }

    [Fact]
    public void NonGenericEnumerationYieldsAllPairs()
    {
        IPostgreSqlStoredSchemaDataSet schema = Schema();

        IEnumerable dataSet = new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
            schema,
            Upserts(schema.Keys)
        );

        Assert.Equal(schema.Count, new NonGenericItems(dataSet).Count());
    }

    [Fact]
    public void TableDataSetExposesWrappedTableSchema()
    {
        IPostgreSqlStoredSchemaDataSet schema = Schema();
        ITable table = schema.Keys.First();

        IPostgreSqlStoredSchemaDataSet dataSet =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                schema,
                Upserts([table])
            );

        Assert.Same(schema[table].TableSchema, dataSet[table].TableSchema);
    }

    [Fact]
    public void TableDataSetExposesWrappedElementType()
    {
        IPostgreSqlStoredSchemaDataSet schema = Schema();
        ITable table = schema.Keys.First();

        IPostgreSqlStoredSchemaDataSet dataSet =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                schema,
                Upserts([table])
            );

        Assert.Equal(schema[table].ElementType, dataSet[table].ElementType);
    }

    [Fact]
    public void TableDataSetExposesWrappedQueryableMembers()
    {
        IPostgreSqlStoredSchemaDataSet schema = Schema();
        ITable table = schema.Keys.First();

        IPostgreSqlStoredSchemaDataSet dataSet =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                schema,
                Upserts([table])
            );

        IStoredTableDataSet tableDataSet = dataSet[table];

        Assert.Same(schema[table].Expression, tableDataSet.Expression);
        Assert.Same(schema[table].Provider, tableDataSet.Provider);
        Assert.Equal(ExistingRowsCount, tableDataSet.AsQueryable().Count());
    }

    [Fact]
    public void TableDataSetEnumeratesWrappedRows()
    {
        IPostgreSqlStoredSchemaDataSet schema = Schema();
        ITable table = schema.Keys.First();

        IPostgreSqlStoredSchemaDataSet dataSet =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                schema,
                Upserts([table])
            );

        Assert.Equal(ExistingRowsCount, dataSet[table].AsEnumerable().Count());
    }

    [Fact]
    public void TableDataSetNonGenericEnumerationYieldsWrappedRows()
    {
        IPostgreSqlStoredSchemaDataSet schema = Schema();
        ITable table = schema.Keys.First();

        IPostgreSqlStoredSchemaDataSet dataSet =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                schema,
                Upserts([table])
            );

        IEnumerable tableDataSet = dataSet[table];

        Assert.Equal(ExistingRowsCount, new NonGenericItems(tableDataSet).Count());
    }

    [Fact]
    public async Task TableDataSetEnumeratesWrappedRowsAsynchronously()
    {
        IPostgreSqlStoredSchemaDataSet schema = Schema();
        ITable table = schema.Keys.First();

        IPostgreSqlStoredSchemaDataSet dataSet =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                schema,
                Upserts([table])
            );

        IRow[] rows = await dataSet[table].ToArrayAsync();

        Assert.Equal(ExistingRowsCount, rows.Length);
    }

    [Fact]
    public async Task AsynchronousEnumerationUpsertsRows()
    {
        IPostgreSqlStoredSchemaDataSet dataSet = RenamingAlice();

        _ = await dataSet[_fixture!.Users].ToArrayAsync();

        Assert.Equal(["1|Renamed|30|true"], _fixture.Stored(_fixture.Users));
    }

    [Fact]
    public void SynchronousEnumerationUpsertsRows()
    {
        IPostgreSqlStoredSchemaDataSet dataSet = RenamingAlice();

        Assert.Equal(ExistingRowsCount, dataSet[_fixture!.Users].AsEnumerable().Count());
        Assert.Equal(["1|Renamed|30|true"], _fixture.Stored(_fixture.Users));
    }

    [Fact]
    public void NonGenericEnumerationUpsertsRows()
    {
        IPostgreSqlStoredSchemaDataSet dataSet = RenamingAlice();

        IEnumerable tableDataSet = dataSet[_fixture!.Users];

        Assert.Equal(ExistingRowsCount, new NonGenericItems(tableDataSet).Count());
        Assert.Equal(["1|Renamed|30|true"], _fixture.Stored(_fixture.Users));
    }

    [Fact]
    public void ProviderUpsertsRows()
    {
        IPostgreSqlStoredSchemaDataSet dataSet = RenamingAlice();

        _ = dataSet[_fixture!.Users].Provider;

        Assert.Equal(["1|Renamed|30|true"], _fixture.Stored(_fixture.Users));
    }

    [Fact]
    public void ExpressionUpsertsRows()
    {
        IPostgreSqlStoredSchemaDataSet dataSet = RenamingAlice();

        _ = dataSet[_fixture!.Users].Expression;

        Assert.Equal(["1|Renamed|30|true"], _fixture.Stored(_fixture.Users));
    }

    [Fact]
    public void ElementTypeUpsertsRows()
    {
        IPostgreSqlStoredSchemaDataSet dataSet = RenamingAlice();

        _ = dataSet[_fixture!.Users].ElementType;

        Assert.Equal(["1|Renamed|30|true"], _fixture.Stored(_fixture.Users));
    }

    [Fact]
    public void TableSchemaUpsertsRows()
    {
        IPostgreSqlStoredSchemaDataSet dataSet = RenamingAlice();

        _ = dataSet[_fixture!.Users].TableSchema;

        Assert.Equal(["1|Renamed|30|true"], _fixture.Stored(_fixture.Users));
    }

    [Fact]
    public void ConnectionIsNotDisposed()
    {
        IPostgreSqlStoredSchemaDataSet dataSet = RenamingAlice();

        _ = dataSet[_fixture!.Users].TableSchema;

        Assert.Equal(System.Data.ConnectionState.Open, dataSet.Connection.State);
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

    private static IEnumerable<TableUpserts> Upserts(IEnumerable<ITable> tables)
    {
        return tables.Select(table => new TableUpserts(
            table,
            new Upsert(
                [table.Columns.First()],
                new Row(
                    new Dictionary<IColumn, IColumn, ICell>(
                        table.Columns,
                        column => column,
                        column => new Cell(new String("1")),
                        column => new ColumnHash(column)
                    )
                )
            )
        ));
    }

    private IPostgreSqlStoredSchemaDataSet RenamingAlice()
    {
        _fixture!.Seed(_fixture.Users, ["1", "Alice", "30", "true"]);
        return new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
            Schema(),
            [
                new TableUpserts(
                    _fixture.Users,
                    new Upsert(
                        [_fixture.Id],
                        new Row(
                            new Dictionary<KeyValuePair<IColumn, ICell>, IColumn, ICell>(
                                [
                                    Cell(_fixture.Id, "1"),
                                    Cell(_fixture.Name, "Renamed"),
                                    Cell(_fixture.Age, "30"),
                                    Cell(_fixture.Active, "true"),
                                ],
                                cell => cell.Key,
                                cell => cell.Value,
                                column => new ColumnHash(column)
                            )
                        )
                    )
                ),
            ]
        );
    }

    private static KeyValuePair<IColumn, ICell> Cell(IColumn column, string value)
    {
        return new KeyValuePair<IColumn, ICell>(column, new Cell(new String(value)));
    }

    private FakeStoredSchemaDataSet Schema()
    {
        return new FakeStoredSchemaDataSet(
            _fixture!.Schema,
            _fixture.Connection,
            table =>
                Enumerable
                    .Range(0, ExistingRowsCount)
                    .Select(_ => new Row(
                        new Dictionary<IColumn, IColumn, ICell>(
                            table.Columns,
                            column => column,
                            column => new Cell(new String("1")),
                            column => new ColumnHash(column)
                        )
                    ))
        );
    }
}
