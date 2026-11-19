using System.Collections;
using Pure.Primitives.Abstractions.Char;
using Pure.Primitives.Abstractions.String;
using Pure.Primitives.String;
using Pure.Primitives.String.Operations;
using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.HashCodes;
using Char = Pure.Primitives.Char.Char;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting;

internal sealed record QuotedColumnName : IString
{
    private readonly IColumn _column;

    public QuotedColumnName(IColumn column)
    {
        _column = column;
    }

    public string TextValue =>
        new WrappedString(
            new DoubleQuoteString(),
            new TrimmedHash(new HexString(new ColumnHash(_column)))
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
