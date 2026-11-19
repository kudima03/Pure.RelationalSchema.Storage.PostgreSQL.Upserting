using Npgsql;
using Pure.Collections.Generic;
using Pure.Primitives.Bool;
using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.Abstractions.ColumnType;
using Pure.RelationalSchema.Abstractions.Schema;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.ColumnType;
using Pure.RelationalSchema.HashCodes;
using Pure.RelationalSchema.Storage.Abstractions;
using Pure.RelationalSchema.Storage.PostgreSQL.Abstractions;
using Pure.RelationalSchema.Storage.PostgreSQL.Creation;
using Pure.RelationalSchema.Storage.PostgreSQL.Insertion;
using Pure.RelationalSchema.Storage.PostgreSQL.Querying;
using Pure.RelationalSchema.Storage.PostgreSQL.Upserting.Tests.Fakes;
using String = Pure.Primitives.String.String;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting.Tests;

using Column = Column.Column;
using ForeignKey = ForeignKey.ForeignKey;
using Index = Index.Index;
using Schema = Schema.Schema;
using Table = Table.Table;

public sealed record PostgreSqlStoredSchemaDataSetWithUpsertedRowsOverQueryingTests
    : IAsyncLifetime,
        IDisposable
{
    private readonly IColumn _userId = new Column(new String("Id"), new IntColumnType());

    private readonly IColumn _userName = new Column(
        new String("Name"),
        new StringColumnType()
    );

    private readonly IColumn _orderId = new Column(new String("Id"), new IntColumnType());

    private readonly IColumn _orderUser = new Column(
        new String("UserId"),
        new IntColumnType()
    );

    private readonly ITable _users;

    private readonly ITable _orders;

    private readonly ISchema _schema;

    private DatabaseFixture? _fixture;

    public PostgreSqlStoredSchemaDataSetWithUpsertedRowsOverQueryingTests()
    {
        _users = new Table(
            new String("Users"),
            [_userId, _userName],
            [new Index(new True(), [_userId])]
        );
        _orders = new Table(new String("Orders"), [_orderId, _orderUser], []);
        _schema = new Schema(
            new String("Shop"),
            [_users, _orders],
            [new ForeignKey(_orders, [_orderUser], _users, [_userId])]
        );
    }

    [Fact]
    public void ReadsUpsertedRowsBackThroughQueryingDataSet()
    {
        IPostgreSqlStoredSchemaDataSet dataSet = Seeded();

        IPostgreSqlStoredSchemaDataSet withUpserted =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                dataSet,
                [
                    new TableUpserts(
                        _users,
                        new Upsert(
                            [_userId],
                            Row(Cell(_userId, "1"), Cell(_userName, "Alicia")),
                            Row(Cell(_userId, "3"), Cell(_userName, "Carol"))
                        )
                    ),
                ]
            );

        Assert.Equal(
            ["Alicia", "Bob O'Brien", "Carol"],
            withUpserted[_users]
                .AsEnumerable()
                .Select(row => row.Cells[_userName].Value.TextValue)
                .Order()
        );
    }

    [Fact]
    public void UniqueIndexViolationFailsAndRollsBackTable()
    {
        IPostgreSqlStoredSchemaDataSet dataSet = Seeded();

        IPostgreSqlStoredSchemaDataSet withUpserted =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                dataSet,
                [
                    new TableUpserts(
                        _users,
                        new Upsert(
                            [_userName],
                            Row(Cell(_userId, "3"), Cell(_userName, "Carol")),
                            Row(Cell(_userId, "1"), Cell(_userName, "Alice again"))
                        )
                    ),
                ]
            );

        PostgresException exception = Assert.Throws<PostgresException>(() =>
            withUpserted[_users].TableSchema
        );

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
        Assert.Equal(
            ["Alice", "Bob O'Brien"],
            dataSet[_users]
                .AsEnumerable()
                .Select(row => row.Cells[_userName].Value.TextValue)
                .Order()
        );
    }

    [Fact]
    public void ForeignKeyViolationFailsAndRollsBackTable()
    {
        IPostgreSqlStoredSchemaDataSet dataSet = Seeded();

        IPostgreSqlStoredSchemaDataSet withUpserted =
            new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
                dataSet,
                [
                    new TableUpserts(
                        _orders,
                        new Upsert(
                            [_orderId],
                            Row(Cell(_orderId, "10"), Cell(_orderUser, "2")),
                            Row(Cell(_orderId, "12"), Cell(_orderUser, "99"))
                        )
                    ),
                ]
            );

        PostgresException exception = Assert.Throws<PostgresException>(() =>
            withUpserted[_orders].TableSchema
        );

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Equal(
            ["1", "1"],
            dataSet[_orders]
                .AsEnumerable()
                .Select(row => row.Cells[_orderUser].Value.TextValue)
        );
    }

    [Fact]
    public void WritesAndMatchesValuesOfEveryColumnType()
    {
        IEnumerable<KeyValuePair<IColumn, string>> values =
        [
            Value("Int", new IntColumnType(), "-42"),
            Value("Bool", new BoolColumnType(), "true"),
            Value("Long", new LongColumnType(), "9000000000"),
            Value("UShort", new UShortColumnType(), "65535"),
            Value("UInt", new UIntColumnType(), "4294967295"),
            Value("ULong", new ULongColumnType(), "18446744073709551615"),
            Value("String", new StringColumnType(), @"it's C:\temp"),
            Value("Date", new DateColumnType(), "2026-02-02"),
            Value("DateTime", new DateTimeColumnType(), "2026-02-02 10:30:00"),
            Value("Time", new TimeColumnType(), "10:30:00"),
            Value("Hash", new DeterminedHashColumnType(), @"\x0a0b"),
            Value("Double", new DoubleColumnType(), "1.5"),
            Value("Float", new FloatColumnType(), "2.25"),
            Value("Uuid", new UuidColumnType(), "6f1c2b0e-0000-4000-8000-000000000001"),
        ];
        IColumn note = new Column(new String("Note"), new StringColumnType());
        ITable typed = new Table(
            new String("Typed"),
            [.. values.Select(value => value.Key), note],
            []
        );
        ISchema schema = new Schema(new String("Types"), [typed], []);
        ISchema created = new PostgreSqlCreatedSchema(schema, _fixture!.Connection);
        _ = created.Tables.ToArray();
        IPostgreSqlStoredSchemaDataSet dataSet = new PostgreSqlStoredSchemaDataSet(
            created,
            _fixture.Connection
        );

        _ = new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
            dataSet,
            [new TableUpserts(typed, new Upsert([note], Typed(values, note, "first")))]
        )[typed].TableSchema;
        _ = new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
            dataSet,
            [
                new TableUpserts(
                    typed,
                    new Upsert(
                        values.Select(value => value.Key),
                        Typed(values, note, "second")
                    )
                ),
            ]
        )[typed].TableSchema;

        Assert.Equal(
            [string.Join("|", [.. values.Select(value => value.Value), "second"])],
            _fixture.Stored(schema, typed)
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

    private static KeyValuePair<IColumn, string> Value(
        string name,
        IColumnType type,
        string value
    )
    {
        return new KeyValuePair<IColumn, string>(
            new Column(new String(name), type),
            value
        );
    }

    private static IRow Typed(
        IEnumerable<KeyValuePair<IColumn, string>> values,
        IColumn note,
        string text
    )
    {
        return Row(
            [
                .. values.Select(value => Cell(value.Key, value.Value)),
                Cell(note, text),
            ]
        );
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

    private IPostgreSqlStoredSchemaDataSet Seeded()
    {
        ISchema created = new PostgreSqlCreatedSchema(_schema, _fixture!.Connection);
        _ = created.Tables.ToArray();

        IPostgreSqlStoredSchemaDataSet dataSet = new PostgreSqlStoredSchemaDataSet(
            created,
            _fixture.Connection
        );

        IPostgreSqlStoredSchemaDataSet withInserted =
            new PostgreSqlStoredSchemaDataSetWithInsertedRows(
                dataSet,
                [
                    new TableRows(
                        _users,
                        [
                            Row(Cell(_userId, "1"), Cell(_userName, "Alice")),
                            Row(Cell(_userId, "2"), Cell(_userName, "Bob O'Brien")),
                        ]
                    ),
                    new TableRows(
                        _orders,
                        [
                            Row(Cell(_orderId, "10"), Cell(_orderUser, "1")),
                            Row(Cell(_orderId, "11"), Cell(_orderUser, "1")),
                        ]
                    ),
                ]
            );

        _ = withInserted[_users].TableSchema;
        _ = withInserted[_orders].TableSchema;

        return dataSet;
    }
}
