using System.Collections;
using Pure.Primitives.Abstractions.Char;
using Pure.Primitives.Abstractions.String;
using Pure.Primitives.String;
using Pure.Primitives.String.Operations;
using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.Storage.Abstractions;
using Char = Pure.Primitives.Char.Char;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting;

internal sealed record TypedLiteral : IString
{
    private readonly KeyValuePair<IColumn, ICell> _cell;

    public TypedLiteral(KeyValuePair<IColumn, ICell> cell)
    {
        _cell = cell;
    }

    public string TextValue =>
        new ConcatenatedString(
            new WrappedString(
                new SingleQuoteString(),
                new SingleQuoteEscapedString(_cell.Value.Value)
            ),
            new ColonString(),
            new ColonString(),
            new PostgreSqlColumnTypeName(_cell.Key.Type)
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
