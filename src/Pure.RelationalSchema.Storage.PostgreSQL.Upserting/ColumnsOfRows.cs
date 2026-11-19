using System.Collections;
using Pure.Collections.Generic;
using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.HashCodes;
using Pure.RelationalSchema.Storage.Abstractions;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting;

internal sealed record ColumnsOfRows : IEnumerable<IColumn>
{
    private readonly IEnumerable<IRow> _rows;

    public ColumnsOfRows(IEnumerable<IRow> rows)
    {
        _rows = rows;
    }

    public IEnumerator<IColumn> GetEnumerator()
    {
        return new Lookup<IColumn, IColumn, IColumn>(
            _rows.SelectMany(row => row.Cells.Keys),
            column => column,
            column => column,
            column => new ColumnHash(column)
        )
            .Select(group => group.Key)
            .GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
