using System.Collections;
using Pure.Primitives.Abstractions.Char;
using Pure.Primitives.Abstractions.String;
using Pure.Primitives.String;
using Pure.Primitives.String.Operations;
using Pure.RelationalSchema.Abstractions.Column;
using Char = Pure.Primitives.Char.Char;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting;

internal sealed record AliasedColumnName : IString
{
    private readonly IString _alias;

    private readonly IColumn _column;

    public AliasedColumnName(IString alias, IColumn column)
    {
        _alias = alias;
        _column = column;
    }

    public string TextValue =>
        new ConcatenatedString(_alias, new DotString(), new QuotedColumnName(_column))
            .TextValue;

    public IEnumerator<IChar> GetEnumerator()
    {
        return TextValue.Select(x => new Char(x)).GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
