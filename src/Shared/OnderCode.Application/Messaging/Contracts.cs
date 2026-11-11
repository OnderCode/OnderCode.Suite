using OnderCode.Application.Results;

namespace OnderCode.Application.Messaging;

/// <summary>Dağıtıcıya gönderilen her mesaj. Yanıt her zaman bir Result türüdür.</summary>
public interface IRequest<TResponse> where TResponse : IResultType<TResponse>
{
}

/// <summary>Durumu değiştiren, değer döndürmeyen işlem (ör. OdaTemizlendi).</summary>
public interface ICommand : IRequest<Result>
{
}

/// <summary>Durumu değiştiren, değer döndüren işlem (ör. RezervasyonOlustur → yeni Id).</summary>
public interface ICommand<TValue> : IRequest<Result<TValue>>
{
}

/// <summary>Yalnızca okuyan işlem.</summary>
public interface IQuery<TValue> : IRequest<Result<TValue>>
{
}

public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IResultType<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

public interface ICommandHandler<in TCommand> : IRequestHandler<TCommand, Result> where TCommand : ICommand
{
}

public interface ICommandHandler<in TCommand, TValue> : IRequestHandler<TCommand, Result<TValue>> where TCommand : ICommand<TValue>
{
}

public interface IQueryHandler<in TQuery, TValue> : IRequestHandler<TQuery, Result<TValue>> where TQuery : IQuery<TValue>
{
}

public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

/// <summary>Her isteğin etrafında çalışan halka (log, doğrulama, yetki, önbellek...). Kayıt sırası dıştan içe uygulanır.</summary>
public interface IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IResultType<TResponse>
{
    Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
}

/// <summary>Ekranların tek giriş noktası: <c>var sonuc = await sender.SendAsync(new OdaEkle(...));</c></summary>
public interface ISender
{
    Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        where TResponse : IResultType<TResponse>;
}
