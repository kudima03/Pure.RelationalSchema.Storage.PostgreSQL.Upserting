using System.Collections;
using Pure.Collections.Generic;
using Pure.HashCodes.Abstractions;
using Pure.Primitives.Abstractions.Char;
using Pure.Primitives.String.Operations;
using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.Abstractions.ColumnType;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.ColumnType;
using Pure.RelationalSchema.HashCodes;
using Pure.RelationalSchema.Storage.Abstractions;
using Pure.RelationalSchema.Storage.PostgreSQL.Upserting.Tests.Fakes;
using String = Pure.Primitives.String.String;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting.Tests;

using Column = Column.Column;
using Table = Table.Table;

public sealed record MergeStatementTests
{
    private static readonly string TableName = new string('t', 64);

    private static readonly string SchemaName = new string('s', 64);

    private static readonly IColumn First = new Column(
        new String("First"),
        new StringColumnType()
    );

    private static readonly IColumn Second = new Column(
        new String("Second"),
        new IntColumnType()
    );

    private static readonly IColumn Third = new Column(
        new String("Third"),
        new BoolColumnType()
    );

    [Fact]
    public void ProducesMergeWithCompositeKey()
    {
        MergeStatement statement = new MergeStatement(
            new String(TableName),
            new String(SchemaName),
            [First, Second],
            [
                Row(Cell(First, "a"), Cell(Second, "1"), Cell(Third, "true")),
                Row(Cell(First, "b"), Cell(Second, "2"), Cell(Third, "false")),
            ]
        );

        Assert.Equal(
            $"MERGE INTO \"{Trimmed(SchemaName)}\".\"{Trimmed(TableName)}\" AS t "
                + "USING (VALUES "
                + "('a'::text, '1'::integer, 'true'::boolean), "
                + "('b'::text, '2'::integer, 'false'::boolean)) "
                + $"AS s(\"{Hash(First)}\", \"{Hash(Second)}\", \"{Hash(Third)}\") "
                + $"ON t.\"{Hash(First)}\" = s.\"{Hash(First)}\" "
                + $"AND t.\"{Hash(Second)}\" = s.\"{Hash(Second)}\" "
                + $"WHEN MATCHED THEN UPDATE SET \"{Hash(Third)}\" = s.\"{Hash(Third)}\" "
                + "WHEN NOT MATCHED THEN INSERT "
                + $"(\"{Hash(First)}\", \"{Hash(Second)}\", \"{Hash(Third)}\") "
                + $"VALUES (s.\"{Hash(First)}\", s.\"{Hash(Second)}\", "
                + $"s.\"{Hash(Third)}\");",
            statement.TextValue
        );
    }

    [Fact]
    public void DoesNothingOnMatchWhenKeyCoversEveryColumn()
    {
        MergeStatement statement = new MergeStatement(
            new String(TableName),
            new String(SchemaName),
            [Third, First, Second],
            [Row(Cell(First, "a"), Cell(Second, "1"), Cell(Third, "true"))]
        );

        Assert.Contains(
            $"AND t.\"{Hash(Second)}\" = s.\"{Hash(Second)}\" "
                + "WHEN MATCHED THEN DO NOTHING WHEN NOT MATCHED THEN INSERT (",
            statement.TextValue
        );
    }

    [Fact]
    public void ComparesKeyColumnsInSuppliedOrder()
    {
        MergeStatement statement = new MergeStatement(
            new String(TableName),
            new String(SchemaName),
            [Second, First],
            [Row(Cell(First, "a"), Cell(Second, "1"), Cell(Third, "true"))]
        );

        Assert.Contains(
            $" ON t.\"{Hash(Second)}\" = s.\"{Hash(Second)}\" "
                + $"AND t.\"{Hash(First)}\" = s.\"{Hash(First)}\" WHEN MATCHED",
            statement.TextValue
        );
    }

    [Fact]
    public void SetsEveryColumnOutsideOfKeyInColumnOrder()
    {
        MergeStatement statement = new MergeStatement(
            new String(TableName),
            new String(SchemaName),
            [Second],
            [Row(Cell(First, "a"), Cell(Second, "1"), Cell(Third, "true"))]
        );

        Assert.Contains(
            $" ON t.\"{Hash(Second)}\" = s.\"{Hash(Second)}\" WHEN MATCHED THEN "
                + $"UPDATE SET \"{Hash(First)}\" = s.\"{Hash(First)}\", "
                + $"\"{Hash(Third)}\" = s.\"{Hash(Third)}\" WHEN NOT MATCHED",
            statement.TextValue
        );
    }

    [Fact]
    public void RendersRepeatedKeyColumnAsIs()
    {
        MergeStatement statement = new MergeStatement(
            new String(TableName),
            new String(SchemaName),
            [First, First],
            [Row(Cell(First, "a"), Cell(Second, "1"))]
        );

        Assert.Contains(
            $" ON t.\"{Hash(First)}\" = s.\"{Hash(First)}\" "
                + $"AND t.\"{Hash(First)}\" = s.\"{Hash(First)}\" WHEN MATCHED THEN "
                + $"UPDATE SET \"{Hash(Second)}\" = s.\"{Hash(Second)}\" WHEN",
            statement.TextValue
        );
    }

    [Fact]
    public void ListsEveryColumnOfRowsOnceAndWritesCellsAsSupplied()
    {
        MergeStatement statement = new MergeStatement(
            new String(TableName),
            new String(SchemaName),
            [First],
            [
                Row(Cell(First, "a"), Cell(Second, "1")),
                Row(Cell(First, "b"), Cell(Third, "true"), Cell(Second, "2")),
            ]
        );

        Assert.Contains(
            "USING (VALUES ('a'::text, '1'::integer), "
                + "('b'::text, 'true'::boolean, '2'::integer)) "
                + $"AS s(\"{Hash(First)}\", \"{Hash(Second)}\", \"{Hash(Third)}\") ON",
            statement.TextValue
        );
    }

    [Fact]
    public void EscapesSingleQuotesInValues()
    {
        MergeStatement statement = new MergeStatement(
            new String(TableName),
            new String(SchemaName),
            [First],
            [Row(Cell(First, "it's")), Row(Cell(First, "'")), Row(Cell(First, "''"))]
        );

        Assert.Contains(
            "USING (VALUES ('it''s'::text), (''''::text), (''''''::text)) AS",
            statement.TextValue
        );
    }

    [Fact]
    public void LeavesBackslashesAsTheyAre()
    {
        MergeStatement statement = new MergeStatement(
            new String(TableName),
            new String(SchemaName),
            [First],
            [Row(Cell(First, @"C:\temp\n"))]
        );

        Assert.Contains(@"USING (VALUES ('C:\temp\n'::text)) AS", statement.TextValue);
    }

    [Fact]
    public void TrimsIdentifiersToPostgreSqlLimit()
    {
        MergeStatement statement = new MergeStatement(
            new String(new string('t', 100)),
            new String(new string('s', 100)),
            [First],
            [Row(Cell(First, "a"))]
        );

        Assert.StartsWith(
            $"MERGE INTO \"{new string('s', 63)}\".\"{new string('t', 63)}\" AS t USING",
            statement.TextValue
        );
    }

    [Fact]
    public void UsesHashOfTableAndKeyAndRowsOfGrouping()
    {
        ITable table = new Table(new String("Test"), [First, Second], []);

        MergeStatement statement = new MergeStatement(
            table,
            new String(SchemaName),
            new Upsert([Second], Row(Cell(First, "a"), Cell(Second, "1")))
        );

        Assert.Equal(
            $"MERGE INTO \"{Trimmed(SchemaName)}\".\"{Hash(new TableHash(table))}\" "
                + "AS t USING (VALUES ('a'::text, '1'::integer)) "
                + $"AS s(\"{Hash(First)}\", \"{Hash(Second)}\") "
                + $"ON t.\"{Hash(Second)}\" = s.\"{Hash(Second)}\" "
                + $"WHEN MATCHED THEN UPDATE SET \"{Hash(First)}\" = s.\"{Hash(First)}\" "
                + "WHEN NOT MATCHED THEN INSERT "
                + $"(\"{Hash(First)}\", \"{Hash(Second)}\") "
                + $"VALUES (s.\"{Hash(First)}\", s.\"{Hash(Second)}\");",
            statement.TextValue
        );
    }

    [Fact]
    public void GroupingWithEmptyKeyThrows()
    {
        ITable table = new Table(new String("Test"), [First, Second], []);

        MergeStatement statement = new MergeStatement(
            table,
            new String(SchemaName),
            new Upsert([], Row(Cell(First, "a"), Cell(Second, "1")))
        );

        _ = Assert.Throws<ArgumentException>(() => statement.TextValue);
    }

    [Fact]
    public void GroupingWithKeyColumnOutsideOfTableThrows()
    {
        ITable table = new Table(new String("Test"), [First, Second], []);

        MergeStatement statement = new MergeStatement(
            table,
            new String(SchemaName),
            new Upsert([First, Third], Row(Cell(First, "a"), Cell(Second, "1")))
        );

        _ = Assert.Throws<ArgumentException>(() => statement.TextValue);
    }

    [Fact]
    public void EnumeratesCharactersOfText()
    {
        MergeStatement statement = new MergeStatement(
            new String(TableName),
            new String(SchemaName),
            [First],
            [Row(Cell(First, "a"), Cell(Second, "1"))]
        );

        Assert.Equal(statement.TextValue, Text(statement));
    }

    [Fact]
    public void EnumeratesCharactersOfTextNonGenerically()
    {
        IEnumerable statement = new MergeStatement(
            new String(TableName),
            new String(SchemaName),
            [First],
            [Row(Cell(First, "a"), Cell(Second, "1"))]
        );

        Assert.Equal(
            ((MergeStatement)statement).TextValue,
            Text(new NonGenericItems(statement).Cast<IChar>())
        );
    }

    [Fact]
    public void ColumnTypeNameFollowsCreationMapping()
    {
        IEnumerable<KeyValuePair<IColumnType, string>> names =
        [
            new(new IntColumnType(), "integer"),
            new(new BoolColumnType(), "boolean"),
            new(new LongColumnType(), "bigint"),
            new(new UShortColumnType(), "integer"),
            new(new UIntColumnType(), "bigint"),
            new(new ULongColumnType(), "numeric"),
            new(new StringColumnType(), "text"),
            new(new DateColumnType(), "date"),
            new(new DateTimeColumnType(), "timestamp"),
            new(new TimeColumnType(), "time"),
            new(new DeterminedHashColumnType(), "bytea"),
            new(new DoubleColumnType(), "double precision"),
            new(new FloatColumnType(), "real"),
            new(new UuidColumnType(), "uuid"),
        ];

        Assert.All(
            names,
            name =>
                Assert.Equal(
                    name.Value,
                    new PostgreSqlColumnTypeName(name.Key).TextValue
                )
        );
    }

    [Fact]
    public void ColumnTypeNameEnumeratesCharactersOfText()
    {
        PostgreSqlColumnTypeName name = new PostgreSqlColumnTypeName(
            new DoubleColumnType()
        );

        Assert.Equal("double precision", Text(name));
    }

    [Fact]
    public void ColumnTypeNameEnumeratesCharactersOfTextNonGenerically()
    {
        IEnumerable name = new PostgreSqlColumnTypeName(new UuidColumnType());

        Assert.Equal("uuid", Text(new NonGenericItems(name).Cast<IChar>()));
    }

    [Fact]
    public void TypedLiteralCastsEscapedValueToTypeOfColumn()
    {
        TypedLiteral literal = new TypedLiteral(
            Cell(new Column(new String("At"), new DateTimeColumnType()), "it's")
        );

        Assert.Equal("'it''s'::timestamp", literal.TextValue);
    }

    [Fact]
    public void TypedLiteralEnumeratesCharactersOfText()
    {
        TypedLiteral literal = new TypedLiteral(Cell(Second, "1"));

        Assert.Equal("'1'::integer", Text(literal));
    }

    [Fact]
    public void TypedLiteralEnumeratesCharactersOfTextNonGenerically()
    {
        IEnumerable literal = new TypedLiteral(Cell(Third, "true"));

        Assert.Equal("'true'::boolean", Text(new NonGenericItems(literal).Cast<IChar>()));
    }

    [Fact]
    public void QuotedColumnNameWrapsTrimmedHashInDoubleQuotes()
    {
        QuotedColumnName name = new QuotedColumnName(First);

        Assert.Equal($"\"{Hash(First)}\"", name.TextValue);
    }

    [Fact]
    public void QuotedColumnNameEnumeratesCharactersOfText()
    {
        QuotedColumnName name = new QuotedColumnName(First);

        Assert.Equal($"\"{Hash(First)}\"", Text(name));
    }

    [Fact]
    public void QuotedColumnNameEnumeratesCharactersOfTextNonGenerically()
    {
        IEnumerable name = new QuotedColumnName(Second);

        Assert.Equal(
            $"\"{Hash(Second)}\"",
            Text(new NonGenericItems(name).Cast<IChar>())
        );
    }

    [Fact]
    public void AliasedColumnNamePrefixesQuotedNameWithAlias()
    {
        AliasedColumnName name = new AliasedColumnName(new String("s"), First);

        Assert.Equal($"s.\"{Hash(First)}\"", name.TextValue);
    }

    [Fact]
    public void AliasedColumnNameEnumeratesCharactersOfText()
    {
        AliasedColumnName name = new AliasedColumnName(new String("t"), First);

        Assert.Equal($"t.\"{Hash(First)}\"", Text(name));
    }

    [Fact]
    public void AliasedColumnNameEnumeratesCharactersOfTextNonGenerically()
    {
        IEnumerable name = new AliasedColumnName(new String("t"), Third);

        Assert.Equal(
            $"t.\"{Hash(Third)}\"",
            Text(new NonGenericItems(name).Cast<IChar>())
        );
    }

    [Fact]
    public void EscapedStringEnumeratesCharactersOfText()
    {
        IEnumerable escaped = new SingleQuoteEscapedString(new String("it's"));

        Assert.Equal("it''s", Text(new NonGenericItems(escaped).Cast<IChar>()));
    }

    [Fact]
    public void EscapedStringEnumeratesCharactersOfTextGenerically()
    {
        SingleQuoteEscapedString escaped = new SingleQuoteEscapedString(new String("'"));

        Assert.Equal("''", Text(escaped));
    }

    [Fact]
    public void TrimmedHashEnumeratesCharactersOfText()
    {
        IEnumerable trimmed = new TrimmedHash(new String(new string('x', 70)));

        Assert.Equal(
            new string('x', 63),
            Text(new NonGenericItems(trimmed).Cast<IChar>())
        );
    }

    [Fact]
    public void TrimmedHashEnumeratesCharactersOfTextGenerically()
    {
        TrimmedHash trimmed = new TrimmedHash(new String(new string('y', 63)));

        Assert.Equal(new string('y', 63), Text(trimmed));
    }

    private static string Text(IEnumerable<IChar> chars)
    {
        return new string([.. chars.Select(x => x.CharValue)]);
    }

    private static string Trimmed(string name)
    {
        return name[..63];
    }

    private static string Hash(IColumn column)
    {
        return Hash(new ColumnHash(column));
    }

    private static string Hash(IDeterminedHash hash)
    {
        string hex = new HexString(hash).TextValue;
        return hex[..Math.Min(63, hex.Length)];
    }

    private static KeyValuePair<IColumn, ICell> Cell(IColumn column, string value)
    {
        return new KeyValuePair<IColumn, ICell>(column, new Cell(new String(value)));
    }

    private static IRow Row(params IEnumerable<KeyValuePair<IColumn, ICell>> cells)
    {
        return new Row(
            new OrderedDictionary<KeyValuePair<IColumn, ICell>, IColumn, ICell>(
                cells,
                x => x.Key,
                x => x.Value,
                x => new ColumnHash(x)
            )
        );
    }
}
