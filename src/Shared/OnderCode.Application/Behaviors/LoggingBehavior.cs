using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OnderCode.Application.Messaging;
using OnderCode.Application.Results;

namespace OnderCode.Application.Behaviors;

/// <summary>En dış halka: süreyi ölçer, başarısız sonuçları uyarı, beklenmeyen hataları hata olarak loglar.</summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILoggerFactory? loggerFactory = null) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TResponse : IResultType<TResponse>
{
    private readonly ILogger _logger = loggerFactory?.CreateLogger("OnderCode.Application") ?? NullLogger.Instance;

    public async Task<TResponse> HandleAsync(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        var name = typeof(TRequest).Name;
        var watch = Stopwatch.StartNew();
        try
        {
            var response = await next();
            if (response.IsSuccess)
                _logger.LogDebug("{Request} tamamlandı ({Elapsed} ms)", name, watch.ElapsedMilliseconds);
            else
                _logger.LogWarning("{Request} başarısız ({Elapsed} ms): {Errors}", name, watch.ElapsedMilliseconds,
                    string.Join(" | ", response.Errors.Select(e => e.Message)));
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Request} beklenmeyen hata ({Elapsed} ms)", name, watch.ElapsedMilliseconds);
            throw;
        }
    }
}
