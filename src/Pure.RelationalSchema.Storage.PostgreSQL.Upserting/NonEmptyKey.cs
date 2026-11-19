using System.Collections;
using Pure.Linq.Conditions;
using Pure.RelationalSchema.Abstractions.Column;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting;

internal sealed record NonEmptyKey : IEnumerable<IColumn>
{
    private readonly IEnumerable<IColumn> _key;

    public NonEmptyKey(IEnumerable<IColumn> key)
    {
        _key = key;
    }

    public IEnumerator<IColumn> GetEnumerator()
    {
        return new EmptyCondition<IColumn>(_key).BoolValue
            ? throw new ArgumentException(
                "The key of upserted rows has no columns; it would match every row."
            )
            : _key.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
