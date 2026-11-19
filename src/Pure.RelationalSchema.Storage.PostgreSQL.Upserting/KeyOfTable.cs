using System.Collections;
using Pure.Collections.Generic;
using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.HashCodes;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting;

internal sealed record KeyOfTable : IEnumerable<IColumn>
{
    private readonly IEnumerable<IColumn> _key;

    private readonly ITable _table;

    public KeyOfTable(IEnumerable<IColumn> key, ITable table)
    {
        _key = key;
        _table = table;
    }

    public IEnumerator<IColumn> GetEnumerator()
    {
        ILookup<IColumn, IColumn> columns = new Lookup<IColumn, IColumn, IColumn>(
            _table.Columns,
            column => column,
            column => column,
            column => new ColumnHash(column)
        );

        return _key.All(columns.Contains)
            ? _key.GetEnumerator()
            : throw new ArgumentException(
                "A key column of upserted rows is not a column of their table."
            );
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
