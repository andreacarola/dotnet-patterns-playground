using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace dotnet_patterns_playground.Lib.TryCatch;

public sealed class ServiceExecution
{
    private DatabaseFacade? _database;
    private CancellationToken _cancellationToken;
    private Action<Exception>? _onError;

    private ServiceExecution() { }

    public static ServiceExecution Start() => new();

    public ServiceExecution WithTransaction(DatabaseFacade database, CancellationToken ct = default)
    { _database = database; _cancellationToken = ct; return this; }

    public ServiceExecution OnError(Action<Exception> onError)
    { _onError = onError; return this; }

    public Task<Result<T>> RunAsync<T>(Func<Task<Result<T>>> action) => (_database, _onError) switch
    {
        ({ }, { }) => _database.WithinTransactionAsync(action, _onError, _cancellationToken),
        (null, { }) => ExceptionUtility.WithinTryAsync(action, _onError),
        ({ }, null) => _database.WithinTransactionAsync(action, _ => { }, _cancellationToken),
        _           => action()
    };

    public Task<Result> RunAsync(Func<Task<Result>> action) => /* stessa logica */
}