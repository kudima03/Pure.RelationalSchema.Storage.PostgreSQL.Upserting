# Pure.RelationalSchema.Storage.PostgreSQL.Upserting

PostgreSQL row upserting for the **Pure** relational schema ecosystem — `MERGE` decorators over `Pure.RelationalSchema.Storage.PostgreSQL.Querying` datasets.

[![.NET build & test](https://github.com/kudima03/Pure.RelationalSchema.Storage.PostgreSQL.Upserting/actions/workflows/build-and-test.yml/badge.svg?branch=main)](https://github.com/kudima03/Pure.RelationalSchema.Storage.PostgreSQL.Upserting/actions/workflows/build-and-test.yml)
[![publish NuGet](https://github.com/kudima03/Pure.RelationalSchema.Storage.PostgreSQL.Upserting/actions/workflows/publish-nuget.yml/badge.svg?branch=main)](https://github.com/kudima03/Pure.RelationalSchema.Storage.PostgreSQL.Upserting/actions/workflows/publish-nuget.yml)
[![NuGet](https://img.shields.io/nuget/v/Pure.RelationalSchema.Storage.PostgreSQL.Upserting)](https://www.nuget.org/packages/Pure.RelationalSchema.Storage.PostgreSQL.Upserting)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

## Overview

`Pure.RelationalSchema.Storage.PostgreSQL.Upserting` upserts rows into a live PostgreSQL database through the storage abstractions defined in `Pure.RelationalSchema.Storage`. `PostgreSqlStoredSchemaDataSetWithUpsertedRows` wraps any `IPostgreSqlStoredSchemaDataSet` (the usual implementation is `PostgreSqlStoredSchemaDataSet` from `Pure.RelationalSchema.Storage.PostgreSQL.Querying`) and a set of rows grouped by table and by key. The upserts of a table are executed lazily, the first time a table dataset of the result is accessed, and only once. After that, the result exposes the wrapped dataset, so the upserted rows can be read back through the same `IStoredTableDataSet`.

Each inner grouping is one upsert: its **key** is the columns that identify a stored row, and its **elements** are the rows to write. A sent row whose key cells equal the stored values of a row updates that row; any other sent row is inserted. Every inner grouping becomes one `MERGE` statement:

```sql
MERGE INTO "<schema>"."<table>" AS t USING (VALUES ('1'::integer, '2026-02-02'::date, 'a'::text), ('2'::integer, '2026-02-03'::date, 'b'::text)) AS s("<c1>", "<c2>", "<c3>") ON t."<c1>" = s."<c1>" AND t."<c2>" = s."<c2>" WHEN MATCHED THEN UPDATE SET "<c3>" = s."<c3>" WHEN NOT MATCHED THEN INSERT ("<c1>", "<c2>", "<c3>") VALUES (s."<c1>", s."<c2>", s."<c3>");
```

and a key that covers every column has nothing to set, so a matched row is kept as it is:

```sql
MERGE INTO "<schema>"."<table>" AS t USING (VALUES ('1'::integer, 'a'::text)) AS s("<c1>", "<c2>") ON t."<c1>" = s."<c1>" AND t."<c2>" = s."<c2>" WHEN MATCHED THEN DO NOTHING WHEN NOT MATCHED THEN INSERT ("<c1>", "<c2>") VALUES (s."<c1>", s."<c2>");
```

All statements of a table are sent together as one command, in the supplied order. Every value is a quoted literal cast to the PostgreSQL type of its column (the mapping of `Pure.RelationalSchema.Storage.PostgreSQL.Creation`): an uncast literal in `VALUES` is `text`, and PostgreSQL cannot compare it with an `integer` column.

Every column is created `NOT NULL`, so the comparisons are exact. Matching works as follows:

- A stored row matches a sent row when every key column holds the same value in both. The key may be any non-empty set of the table's columns, in any order: just the identifying columns, or the whole row.
- Every matching stored row is updated, so a key that is not unique in the table updates all rows that share it. No index is required.
- The columns outside of the key are set to the sent values; the key columns are not changed. When the key covers every column, a matched row is left as it is (`DO NOTHING`).
- A sent row that matches no stored row is inserted.
- Two sent rows of one grouping must not match the same stored row: PostgreSQL rejects that (`MERGE command cannot affect row a second time`, SQLSTATE `21000`) and nothing of the table is applied. The rows are not de-duplicated. Two sent rows with the same new key are both inserted (and rejected by a unique index, if the table has one).
- Groupings run one after another in the supplied order, so a later grouping sees the effect of an earlier one, and a table may receive several groupings with different keys.
- A grouping without rows is skipped, but its key is still checked. Nothing is executed when a table has no rows to upsert.

Rows are written as Insertion writes them: the column list is every column that a row of the grouping has a cell for, and each row's cells are written in the order its `Cells` enumerate them. Every row should therefore have a cell for each column of the table, in the same order (rows built the same way over the table's columns do). A row with fewer cells than the column list is rejected by PostgreSQL (`VALUES lists must all be the same length`). A column that no row has a cell for is left out of the statement: a matched row keeps its stored value, and an inserted row violates `NOT NULL`.

The key of every grouping, including one without rows, is checked when the table's command is built, on first access: an empty key would turn into `ON TRUE` and overwrite every stored row, and a key column that is not a column of the table (compared by `ColumnHash`) would be rejected by PostgreSQL; both throw `ArgumentException` before anything is sent.

A table's command runs in one implicit transaction: when any of its statements fails, none of them is applied and the `PostgresException` is thrown from the member that triggered the execution. This is how foreign keys and unique indexes (as created by `Pure.RelationalSchema.Storage.PostgreSQL.Creation`) are enforced: the statements are plain, and PostgreSQL's constraints decide the outcome. The package does not check them itself and does not cascade. A cell whose column does not belong to the table is rejected by PostgreSQL in the same way (`column … does not exist`). Commands of different tables are independent.

The result exposes **only the tables present in the supplied groupings**, not every table of the wrapped dataset. This matches `PostgreSqlStoredSchemaDataSetWithInsertedRows`, `PostgreSqlStoredSchemaDataSetWithDeletedRows` and `PostgreSqlStoredSchemaDataSetWithUpdatedRows` from the Insertion, Deletion and Updating packages.

This package covers upserts only. Creating the tables, reading rows, inserting, deleting and updating rows are handled by the related packages listed below. The tables must already exist with the hash-based names produced by `Pure.RelationalSchema.Storage.PostgreSQL.Creation`. Schema, table and column identifiers are the first 63 hexadecimal characters of the hashes from `Pure.RelationalSchema.HashCodes`, not the model names. The caller owns the `IDbConnection`: it must be opened before use, and this package never disposes it.

## Public API

| Type | Kind | Description |
|------|------|-------------|
| `PostgreSqlStoredSchemaDataSetWithUpsertedRows` | record | Wraps an `IPostgreSqlStoredSchemaDataSet` and upserts rows into the corresponding tables; each grouping updates the stored rows matching its key columns and inserts the other rows; implements `IPostgreSqlStoredSchemaDataSet` |

```csharp
public PostgreSqlStoredSchemaDataSetWithUpsertedRows(
    IPostgreSqlStoredSchemaDataSet dataset,
    IEnumerable<IGrouping<ITable, IGrouping<IEnumerable<IColumn>, IRow>>> rows)
```

The outer key is the table, the inner key is the key columns, and the inner elements are the rows to upsert. The constructor takes any `IPostgreSqlStoredSchemaDataSet`. The table-level decorator, `MergeStatement`, the key rules and the hashing, escaping and type-name helpers are internal. `IPostgreSqlStoredSchemaDataSet` comes from `Pure.RelationalSchema.Storage.PostgreSQL.Abstractions`; this package does not depend on Querying, Creation, Insertion, Deletion or Updating.

## Dependencies

- [`Pure.RelationalSchema.Storage.PostgreSQL.Abstractions`](https://github.com/kudima03/Pure.RelationalSchema.Storage.PostgreSQL.Abstractions/tree/0.1.0-preview.0.2.0) — provides `IPostgreSqlStoredSchemaDataSet`
- [`Pure.RelationalSchema.Storage.Abstractions`](https://github.com/kudima03/Pure.RelationalSchema.Storage.Abstractions/tree/0.1.0-preview.4.2.0) — storage abstractions (`IStoredSchemaDataSet`, `IStoredTableDataSet`, `IRow`, `ICell`) that this package implements and consumes
- [`Pure.RelationalSchema`](https://github.com/kudima03/Pure.RelationalSchema/tree/2.2.0) — the column types mapped to PostgreSQL type names for the casts
- [`Pure.RelationalSchema.HashCodes`](https://github.com/kudima03/Pure.RelationalSchema.HashCodes/tree/3.4.0) — hash codes used to derive PostgreSQL object names and to compare columns
- [`Pure.Primitives.Abstractions`](https://github.com/kudima03/Pure.Primitives.Abstractions/tree/4.4.0) — base interfaces for primitive types (`IString`, `IBool`, etc.)
- [`Pure.Primitives`](https://github.com/kudima03/Pure.Primitives/tree/3.8.0) — concrete primitive type implementations
- [`Pure.Primitives.String.Operations`](https://github.com/kudima03/Pure.Primitives.String.Operations/tree/1.8.0) — string composition utilities used to build SQL statements
- [`Pure.Primitives.Choices`](https://github.com/kudima03/Pure.Primitives.Choices/tree/1.4.0) — `StringChoice`, used to write `DO NOTHING` when the key covers every column
- [`Pure.Primitives.Switches`](https://github.com/kudima03/Pure.Primitives.Switches/tree/2.0.0) — `StringSwitch`, used to map column types to PostgreSQL type names
- [`Pure.Linq.Conditions`](https://github.com/kudima03/Pure.Linq.Conditions/tree/0.1.0-preview.0.4.0) — `EmptyCondition`, which tells whether a key or the columns to set are empty
- [`Pure.Collections.Generic`](https://github.com/kudima03/Pure.Collections.Generic/tree/0.1.0-preview.3.1.0) — generic collection implementations (`OrderedDictionary` keyed by `TableHash`, `Lookup` keyed by `ColumnHash`)

## Target Frameworks

- .NET 8
- .NET 9
- .NET 10
- .NET 11

## Installation

```
dotnet add package Pure.RelationalSchema.Storage.PostgreSQL.Upserting
```

This package does not create tables or read rows itself. The example below also uses `Pure.RelationalSchema.Storage.PostgreSQL.Creation` to create the tables and `Pure.RelationalSchema.Storage.PostgreSQL.Querying` for the wrapped dataset; neither is a dependency of this package.

## Usage

```csharp
using System.Collections;
using System.Data;
using Npgsql;
using Pure.Collections.Generic;
using Pure.Primitives.Bool;
using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.Abstractions.Schema;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.ColumnType;
using Pure.RelationalSchema.HashCodes;
using Pure.RelationalSchema.Storage;
using Pure.RelationalSchema.Storage.Abstractions;
using Pure.RelationalSchema.Storage.PostgreSQL.Abstractions;
using Pure.RelationalSchema.Storage.PostgreSQL.Creation;
using Pure.RelationalSchema.Storage.PostgreSQL.Querying;
using Pure.RelationalSchema.Storage.PostgreSQL.Upserting;
using Column = Pure.RelationalSchema.Column.Column;
using Index = Pure.RelationalSchema.Index.Index;
using Schema = Pure.RelationalSchema.Schema.Schema;
using String = Pure.Primitives.String.String;
using Table = Pure.RelationalSchema.Table.Table;

IDbConnection connection = new NpgsqlConnection(connectionString);
connection.Open();

IColumn idColumn = new Column(new String("Id"), new IntColumnType());
IColumn nameColumn = new Column(new String("Name"), new StringColumnType());
ITable usersTable = new Table(
    new String("Users"),
    [idColumn, nameColumn],
    [new Index(new True(), [idColumn])]
);
ISchema schema = new Schema(new String("Shop"), [usersTable], []);

ISchema createdSchema = new PostgreSqlCreatedSchema(schema, connection);
_ = createdSchema.Tables.ToArray();

IPostgreSqlStoredSchemaDataSet dataSet =
    new PostgreSqlStoredSchemaDataSet(createdSchema, connection);

// Nothing is stored yet, so both rows are inserted.
IPostgreSqlStoredSchemaDataSet first = new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
    dataSet,
    [
        new TableUpserts(
            usersTable,
            [new Upsert([idColumn], [User("1", "Alice"), User("2", "Bob")])]
        ),
    ]
);

// The decorator is lazy: the MERGE runs on first access to the table dataset.
_ = first[usersTable].TableSchema;

// Alice is matched by her id and renamed; Carol is not stored, so she is inserted.
IPostgreSqlStoredSchemaDataSet second = new PostgreSqlStoredSchemaDataSetWithUpsertedRows(
    dataSet,
    [
        new TableUpserts(
            usersTable,
            [new Upsert([idColumn], [User("1", "Alicia"), User("3", "Carol O'Brien")])]
        ),
    ]
);

foreach (IRow row in second[usersTable])
{
    Console.WriteLine(row.Cells[nameColumn].Value.TextValue); // Alicia, Bob, Carol O'Brien
}

IRow User(string id, string name)
{
    return new Row(
        new Dictionary<KeyValuePair<IColumn, ICell>, IColumn, ICell>(
            [
                new KeyValuePair<IColumn, ICell>(idColumn, new Cell(new String(id))),
                new KeyValuePair<IColumn, ICell>(nameColumn, new Cell(new String(name))),
            ],
            cell => cell.Key,
            cell => cell.Value,
            column => new ColumnHash(column)
        )
    );
}

// One upsert: the key columns identify the stored rows, the elements are the rows to write.
internal sealed class Upsert : IGrouping<IEnumerable<IColumn>, IRow>
{
    private readonly IEnumerable<IRow> _rows;

    public Upsert(IEnumerable<IColumn> key, IEnumerable<IRow> rows)
    {
        Key = key;
        _rows = rows;
    }

    public IEnumerable<IColumn> Key { get; }

    public IEnumerator<IRow> GetEnumerator()
    {
        return _rows.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}

// The upserts of one table, in the order they are executed.
internal sealed class TableUpserts : IGrouping<ITable, IGrouping<IEnumerable<IColumn>, IRow>>
{
    private readonly IEnumerable<IGrouping<IEnumerable<IColumn>, IRow>> _upserts;

    public TableUpserts(ITable key, IEnumerable<IGrouping<IEnumerable<IColumn>, IRow>> upserts)
    {
        Key = key;
        _upserts = upserts;
    }

    public ITable Key { get; }

    public IEnumerator<IGrouping<IEnumerable<IColumn>, IRow>> GetEnumerator()
    {
        return _upserts.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
```

`GroupBy` cannot be used to build the groupings: it hashes the key, and neither `ITable` implementations nor key enumerables support value-based `GetHashCode`, so supply your own `IGrouping` types as above. Every cell value is an `IString`, written as a literal cast to the column's PostgreSQL type, so it must be valid input for that type (`2026-02-02` for a date, `\x0a0b` for a hash, and so on).

## Known limitations

- **Not concurrency-safe like `ON CONFLICT`.** `MERGE` decides between update and insert from the rows it sees; it takes no lock on keys that are not stored yet. Two concurrent upserts of the same new key both insert it, unless a unique index over the key exists, in which case one of them fails with a unique violation (`23505`). Declare a unique index over the key columns (`IIndex.IsUnique`) and retry on `23505` when upserts of the same key can run concurrently.
- **Values are inlined.** Values are written as quoted string literals with embedded single quotes doubled; backslashes are kept as they are (`standard_conforming_strings` is on by default). Values are not sent as parameters, so do not pass untrusted input.
- **One statement per grouping.** Each grouping is one `MERGE` with all its rows in `VALUES`; a table's statements travel in one command, so a very large batch produces very large SQL text.
- **Few checks of its own.** Only the key is checked (non-empty, columns of the table). Rows with missing cells, cells of other columns, two rows matching one stored row and constraint violations are reported by PostgreSQL when the table's command runs, not when the decorator is built.
- **Supply each table once.** The groupings are keyed by `TableHash`; two groupings for the same table make the lookups (`this[]`, `ContainsKey`, `TryGetValue`, `Count`) throw. Put all upserts of a table into one grouping, as with Insertion, Deletion and Updating.
- **Only supplied tables are exposed.** Tables of the wrapped dataset that have no grouping are not exposed by the result.

## Related packages

- `Pure.RelationalSchema.Storage.PostgreSQL.Creation` — creates the schema and tables this package upserts into; its column type mapping is the one used for the casts
- `Pure.RelationalSchema.Storage.PostgreSQL.Querying` — reads rows from those tables; `PostgreSqlStoredSchemaDataSet` is the usual dataset to wrap
- `Pure.RelationalSchema.Storage.PostgreSQL.Insertion` — inserts rows into those tables
- `Pure.RelationalSchema.Storage.PostgreSQL.Updating` — updates rows matching a set of cells
- `Pure.RelationalSchema.Storage.PostgreSQL.Deletion` — deletes rows from those tables
- `Pure.RelationalSchema.Storage.PostgreSQL.Abstractions` — provides `IPostgreSqlStoredSchemaDataSet`
