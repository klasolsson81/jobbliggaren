using System.Data;
using System.Data.Common;

namespace Jobbliggaren.Infrastructure.Auth.Access;

public sealed class ProtectedAccountTransaction
{
    private DbConnection? _connection;
    private DbTransaction? _transaction;
    private Action? _poison;

    public void Enter(DbConnection connection, DbTransaction transaction, Action poison)
    {
        if (_connection is not null)
            throw new InvalidOperationException("A protected transaction is already active.");
        _connection = connection;
        _transaction = transaction;
        _poison = poison;
    }

    public void Exit()
    {
        _connection = null;
        _transaction = null;
        _poison = null;
    }

    public void RefuseReopening()
    {
        if (_connection is not null)
            Refuse();
    }

    public void AssertCommand(DbCommand command)
    {
        if (_connection is not null)
            AssertTransaction(command.Connection, command.Transaction);
    }

    public void AssertTransaction(DbConnection? connection, DbTransaction? transaction)
    {
        if (_connection is not null && (connection?.State != ConnectionState.Open
            || !ReferenceEquals(connection, _connection) || !ReferenceEquals(transaction, _transaction)))
            Refuse();
    }

    private void Refuse()
    {
        _poison!();
        throw new InvalidOperationException("The protected database transaction was lost; reopening or replacing it is refused.");
    }
}
