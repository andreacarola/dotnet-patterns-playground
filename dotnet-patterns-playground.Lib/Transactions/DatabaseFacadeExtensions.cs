namespace dotnet_patterns_playground.Lib;
internal static class DatabaseFacadeExtensions
{
    /// <summary>
    /// Begins a new transaction if no ambient transaction is already active on the <see cref="DatabaseFacade"/>.
    /// If a transaction is already active, returns a <see cref="NullDbContextTransaction"/> (null object pattern)
    /// whose Commit, Rollback and Dispose are all no-ops, leaving lifecycle control to the transaction owner.
    /// </summary>
    public static async Task<IDbContextTransaction> BeginTransactionIfNoneActiveAsync(
        this DatabaseFacade database,
        CancellationToken cancellationToken = default)
    {
        if (database.CurrentTransaction is not null)
            return new NullDbContextTransaction();

        return await database.BeginTransactionAsync(cancellationToken);
    }
}