using System.Collections;
using Pure.Linq.Conditions;
using Pure.Primitives.Abstractions.Char;
using Pure.Primitives.Abstractions.String;
using Pure.Primitives.Choices.String;
using Pure.Primitives.String;
using Pure.Primitives.String.Operations;
using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.HashCodes;
using Pure.RelationalSchema.Storage.Abstractions;
using Char = Pure.Primitives.Char.Char;
using String = Pure.Primitives.String.String;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting;

internal sealed record MergeStatement : IString
{
    private readonly IString _tableName;

    private readonly IString _schemaName;

    private readonly IEnumerable<IColumn> _key;

    private readonly IEnumerable<IRow> _rows;

    private readonly IEnumerable<IColumn> _columns;

    private readonly IString _target;

    private readonly IString _source;

    public MergeStatement(
        ITable table,
        IString schemaName,
        IGrouping<IEnumerable<IColumn>, IRow> rows
    )
        : this(
            new HexString(new TableHash(table)),
            schemaName,
            new KeyOfTable(new NonEmptyKey(rows.Key), table),
            rows
        )
    { }

    public MergeStatement(
        IString tableName,
        IString schemaName,
        IEnumerable<IColumn> key,
        IEnumerable<IRow> rows
    )
        : this(
            tableName,
            schemaName,
            key,
            rows,
            new ColumnsOfRows(rows),
            new String("t"),
            new String("s")
        )
    { }

    private MergeStatement(
        IString tableName,
        IString schemaName,
        IEnumerable<IColumn> key,
        IEnumerable<IRow> rows,
        IEnumerable<IColumn> columns,
        IString target,
        IString source
    )
    {
        _tableName = tableName;
        _schemaName = schemaName;
        _key = key;
        _rows = rows;
        _columns = columns;
        _target = target;
        _source = source;
    }

    public string TextValue =>
        new ConcatenatedString(
            new WhitespaceJoinedString(
                new String("MERGE INTO"),
                new JoinedString(
                    new DotString(),
                    [
                        new WrappedString(
                            new DoubleQuoteString(),
                            new TrimmedHash(_schemaName)
                        ),
                        new WrappedString(
                            new DoubleQuoteString(),
                            new TrimmedHash(_tableName)
                        ),
                    ]
                ),
                new String("AS"),
                _target,
                new String("USING"),
                new WrappedString(
                    new LeftRoundBracketString(),
                    new WhitespaceJoinedString(
                        new String("VALUES"),
                        new JoinedString(
                            new ConcatenatedString(
                                new CommaString(),
                                new WhitespaceString()
                            ),
                            _rows.Select(row => new WrappedString(
                                new LeftRoundBracketString(),
                                new JoinedString(
                                    new ConcatenatedString(
                                        new CommaString(),
                                        new WhitespaceString()
                                    ),
                                    row.Cells.Select(cell => new TypedLiteral(cell))
                                ),
                                new RightRoundBracketString()
                            ))
                        )
                    ),
                    new RightRoundBracketString()
                ),
                new String("AS"),
                new ConcatenatedString(
                    _source,
                    new WrappedString(
                        new LeftRoundBracketString(),
                        new JoinedString(
                            new ConcatenatedString(
                                new CommaString(),
                                new WhitespaceString()
                            ),
                            _columns.Select(column => new QuotedColumnName(column))
                        ),
                        new RightRoundBracketString()
                    )
                ),
                new String("ON"),
                new JoinedString(
                    new WrappedString(new WhitespaceString(), new String("AND")),
                    _key.Select(column => new WhitespaceJoinedString(
                        new AliasedColumnName(_target, column),
                        new EqualsSignString(),
                        new AliasedColumnName(_source, column)
                    ))
                ),
                new String("WHEN MATCHED THEN"),
                new StringChoice(
                    new EmptyCondition<IColumn>(new ColumnsOutsideOfKey(_columns, _key)),
                    new String("DO NOTHING"),
                    new WhitespaceJoinedString(
                        new String("UPDATE SET"),
                        new JoinedString(
                            new ConcatenatedString(
                                new CommaString(),
                                new WhitespaceString()
                            ),
                            new ColumnsOutsideOfKey(_columns, _key).Select(
                                column => new WhitespaceJoinedString(
                                    new QuotedColumnName(column),
                                    new EqualsSignString(),
                                    new AliasedColumnName(_source, column)
                                )
                            )
                        )
                    )
                ),
                new String("WHEN NOT MATCHED THEN INSERT"),
                new WrappedString(
                    new LeftRoundBracketString(),
                    new JoinedString(
                        new ConcatenatedString(new CommaString(), new WhitespaceString()),
                        _columns.Select(column => new QuotedColumnName(column))
                    ),
                    new RightRoundBracketString()
                ),
                new String("VALUES"),
                new WrappedString(
                    new LeftRoundBracketString(),
                    new JoinedString(
                        new ConcatenatedString(new CommaString(), new WhitespaceString()),
                        _columns.Select(column => new AliasedColumnName(_source, column))
                    ),
                    new RightRoundBracketString()
                )
            ),
            new SemicolonString()
        ).TextValue;

    public IEnumerator<IChar> GetEnumerator()
    {
        return TextValue.Select(x => new Char(x)).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
