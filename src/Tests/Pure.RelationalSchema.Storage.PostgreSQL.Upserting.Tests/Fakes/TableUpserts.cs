using System.Collections;
using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.Storage.Abstractions;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting.Tests.Fakes;

internal sealed record TableUpserts
    : IGrouping<ITable, IGrouping<IEnumerable<IColumn>, IRow>>
{
    private readonly IEnumerable<IGrouping<IEnumerable<IColumn>, IRow>> _upserts;

    public TableUpserts(
        ITable key,
        params IEnumerable<IGrouping<IEnumerable<IColumn>, IRow>> upserts
    )
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
