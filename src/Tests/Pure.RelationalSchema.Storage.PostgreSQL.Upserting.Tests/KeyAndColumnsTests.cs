using Pure.Collections.Generic;
using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.ColumnType;
using Pure.RelationalSchema.HashCodes;
using Pure.RelationalSchema.Storage.Abstractions;
using Pure.RelationalSchema.Storage.PostgreSQL.Upserting.Tests.Fakes;
using String = Pure.Primitives.String.String;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting.Tests;

using Column = Column.Column;
using Table = Table.Table;

public sealed record KeyAndColumnsTests
{
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

    private static readonly ITable Table = new Table(
        new String("Test"),
        [First, Second],
        []
    );

    [Fact]
    public void NonEmptyKeyEnumeratesKey()
    {
        Assert.Equal([Second, First], new NonEmptyKey([Second, First]));
    }

    [Fact]
    public void NonEmptyKeyEnumeratesKeyNonGenerically()
    {
        Assert.Equal(
            [First],
            new NonGenericItems(new NonEmptyKey([First])).Cast<IColumn>()
        );
    }

    [Fact]
    public void NonEmptyKeyThrowsOnEmptyKey()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new NonEmptyKey([]).ToArray()
        );

        Assert.Equal(
            "The key of upserted rows has no columns; it would match every row.",
            exception.Message
        );
    }

    [Fact]
    public void KeyOfTableEnumeratesKeyOfColumnsOfTable()
    {
        Assert.Equal(
            [Second, First, Second],
            new KeyOfTable([Second, First, Second], Table)
        );
    }

    [Fact]
    public void KeyOfTableEnumeratesKeyNonGenerically()
    {
        Assert.Equal(
            [Second],
            new NonGenericItems(new KeyOfTable([Second], Table)).Cast<IColumn>()
        );
    }

    [Fact]
    public void KeyOfTableMatchesColumnsByContent()
    {
        IColumn copy = new Column(new String("First"), new StringColumnType());

        _ = Assert.Single(new KeyOfTable([copy], Table));
    }

    [Fact]
    public void KeyOfTableThrowsOnColumnOutsideOfTable()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new KeyOfTable([First, Third], Table).ToArray()
        );

        Assert.Equal(
            "A key column of upserted rows is not a column of their table.",
            exception.Message
        );
    }

    [Fact]
    public void KeyOfTableThrowsOnColumnOfSameNameAndOtherType()
    {
        IColumn other = new Column(new String("First"), new IntColumnType());

        _ = Assert.Throws<ArgumentException>(() =>
            new KeyOfTable([other], Table).ToArray()
        );
    }

    [Fact]
    public void ColumnsOfRowsListsEveryColumnOnceInFirstSeenOrder()
    {
        Assert.Equal(
            [Second, First, Third],
            new ColumnsOfRows(
                [
                    Row(Cell(Second, "1")),
                    Row(Cell(First, "a"), Cell(Second, "2")),
                    Row(Cell(Third, "true"), Cell(First, "b")),
                ]
            )
        );
    }

    [Fact]
    public void ColumnsOfRowsIsEmptyWithoutRows()
    {
        Assert.Empty(new ColumnsOfRows([]));
    }

    [Fact]
    public void ColumnsOfRowsEnumeratesColumnsNonGenerically()
    {
        Assert.Equal(
            [First],
            new NonGenericItems(new ColumnsOfRows([Row(Cell(First, "a"))]))
                .Cast<IColumn>()
        );
    }

    [Fact]
    public void ColumnsOutsideOfKeyKeepOrderOfColumns()
    {
        Assert.Equal(
            [First, Third],
            new ColumnsOutsideOfKey([First, Second, Third], [Second])
        );
    }

    [Fact]
    public void ColumnsOutsideOfKeyMatchKeyByContent()
    {
        IColumn copy = new Column(new String("Second"), new IntColumnType());

        Assert.Equal([First], new ColumnsOutsideOfKey([First, Second], [copy, copy]));
    }

    [Fact]
    public void ColumnsOutsideOfKeyAreEmptyWhenKeyCoversEveryColumn()
    {
        Assert.Empty(new ColumnsOutsideOfKey([First, Second], [Second, Third, First]));
    }

    [Fact]
    public void ColumnsOutsideOfKeyEnumerateColumnsNonGenerically()
    {
        Assert.Equal(
            [Third],
            new NonGenericItems(new ColumnsOutsideOfKey([First, Third], [First]))
                .Cast<IColumn>()
        );
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
