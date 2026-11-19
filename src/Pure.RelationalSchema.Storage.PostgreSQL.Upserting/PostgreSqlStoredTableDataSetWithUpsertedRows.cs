using System.Collections;
using System.Data;
using System.Linq.Expressions;
using Pure.Primitives.Abstractions.Bool;
using Pure.Primitives.Abstractions.String;
using Pure.Primitives.Bool;
using Pure.Primitives.String.Operations;
using Pure.RelationalSchema.Abstractions.Column;
using Pure.RelationalSchema.Abstractions.Table;
using Pure.RelationalSchema.Storage.Abstractions;

namespace Pure.RelationalSchema.Storage.PostgreSQL.Upserting;

internal sealed record PostgreSqlStoredTableDataSetWithUpsertedRows : IStoredTableDataSet
{
    private readonly IStoredTableDataSet _dataSet;

    private readonly Lazy<IBool> _rowsUpserted;

    public PostgreSqlStoredTableDataSetWithUpsertedRows(
        IStoredTableDataSet dataSet,
        IDbConnection connection,
        IString schemaName,
        IEnumerable<IGrouping<IEnumerable<IColumn>, IRow>> rows
    )
    {
        _dataSet = dataSet;
        _rowsUpserted = new Lazy<IBool>(() =>
        {
            IEnumerable<IString> statements =
            [
                .. rows
                    // The key is checked even when the grouping has no rows.
                    .Where(group =>
                        new KeyOfTable(new NonEmptyKey(group.Key), dataSet.TableSchema)
                            .Any()
                    )
                    .Where(group => group.Any())
                    .Select(group => new MergeStatement(
                        dataSet.TableSchema,
                        schemaName,
                        group
                    )),
            ];

            if (!statements.Any())
            {
                return new True();
            }

            using IDbCommand command = connection.CreateCommand();
            command.CommandText = new WhitespaceJoinedString(statements).TextValue;
            _ = command.ExecuteNonQuery();
            return new True();
        });
    }

    public ITable TableSchema =>
        _rowsUpserted.Value.BoolValue
            ? _dataSet.TableSchema
            : throw new ArgumentException("Value was not upserted.");

    public Type ElementType =>
        _rowsUpserted.Value.BoolValue
            ? _dataSet.ElementType
            : throw new ArgumentException("Value was not upserted.");

    public Expression Expression =>
        _rowsUpserted.Value.BoolValue
            ? _dataSet.Expression
            : throw new ArgumentException("Value was not upserted.");

    public IQueryProvider Provider =>
        _rowsUpserted.Value.BoolValue
            ? _dataSet.Provider
            : throw new ArgumentException("Value was not upserted.");

    public IEnumerator<IRow> GetEnumerator()
    {
        return _rowsUpserted.Value.BoolValue
            ? _dataSet.GetEnumerator()
            : throw new ArgumentException("Value was not upserted.");
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public IAsyncEnumerator<IRow> GetAsyncEnumerator(
        CancellationToken cancellationToken = default
    )
    {
        return _rowsUpserted.Value.BoolValue
            ? _dataSet.GetAsyncEnumerator(cancellationToken)
            : throw new ArgumentException("Value was not upserted.");
    }
}
