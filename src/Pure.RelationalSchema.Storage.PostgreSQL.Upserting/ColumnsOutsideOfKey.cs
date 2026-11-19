using System.Collections;
using Pure.Collections.Generic;
using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.HashCodes;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting;

internal sealed record ColumnsOutsideOfKey : IEnumerable<IColumn>
{
    private readonly IEnumerable<IColumn> _columns;

    private readonly IEnumerable<IColumn> _key;

    public ColumnsOutsideOfKey(IEnumerable<IColumn> columns, IEnumerable<IColumn> key)
    {
        _columns = columns;
        _key = key;
    }

    public IEnumerator<IColumn> GetEnumerator()
    {
        ILookup<IColumn, IColumn> key = new Lookup<IColumn, IColumn, IColumn>(
            _key,
            column => column,
            column => column,
            column => new ColumnHash(column)
        );

        return _columns.Where(column => !key.Contains(column)).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
