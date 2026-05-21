using dotnet_patterns_playground.Lib;

/// <summary>
/// Null object pattern implementation of <see cref="IDbContextTransaction"/>.
/// Used when a caller wants to participate in an existing ambient transaction without owning it.
/// All operations (Commit, Rollback, Dispose) are intentional no-ops.
/// </summary>
internal sealed class NullDbContextTransaction : IDbContextTransaction
{
    public Guid TransactionId { get; } = Guid.NewGuid();

    public void Commit() { }

    public Task CommitAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public void Rollback() { }

    public Task RollbackAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public DbTransaction GetDbTransaction()
        => throw new InvalidOperationException("NullDbContextTransaction does not wrap a real DbTransaction.");

    public void Dispose() { }

    public ValueTask DisposeAsync()
        => ValueTask.CompletedTask;
}
