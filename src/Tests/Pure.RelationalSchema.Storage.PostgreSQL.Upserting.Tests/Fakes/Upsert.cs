using System.Collections;
using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.Storage.Abstractions;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting.Tests.Fakes;

internal sealed record Upsert : IGrouping<IEnumerable<IColumn>, IRow>
{
    private readonly IEnumerable<IRow> _rows;

    public Upsert(IEnumerable<IColumn> key, params IEnumerable<IRow> rows)
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
