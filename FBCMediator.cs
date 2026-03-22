using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace FBC.Mediator;

public class FBCMediator : IMediator
{
    private class VoidMarker { }

    private static readonly ConcurrentDictionary<string, object> _handlerCache = new();

    private static readonly MethodInfo GetRequiredServiceGenericMethod = typeof(ServiceProviderServiceExtensions)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .First(m =>
            m.Name == nameof(ServiceProviderServiceExtensions.GetRequiredService) &&
            m.IsGenericMethodDefinition &&
            m.GetParameters().Length == 1 &&
            m.GetParameters()[0].ParameterType == typeof(IServiceProvider))
        .GetGenericMethodDefinition();

    private readonly IServiceProvider _serviceProvider;

    public FBCMediator(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = GetCacheKey(request.GetType(), typeof(TResponse));
        var invoker = (Func<IServiceProvider, object, CancellationToken, Task<TResponse>>)_handlerCache.GetOrAdd(key, _ => BuildRequestInvoker<TResponse>(request.GetType()));
        return invoker(_serviceProvider, request, cancellationToken);
    }

    public Task Send(IRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var key = GetCacheKey(request.GetType(), typeof(VoidMarker));
        var invoker = (Func<IServiceProvider, object, CancellationToken, Task>)_handlerCache.GetOrAdd(key, _ => BuildVoidRequestInvoker(request.GetType()));
        return invoker(_serviceProvider, request, cancellationToken);
    }

    private static string GetCacheKey(Type requestType, Type responseType) => requestType.FullName + "=>" + responseType.FullName;

    private static Func<IServiceProvider, object, CancellationToken, Task<TResponse>> BuildRequestInvoker<TResponse>(Type requestType)
    {
        var handlerType = typeof(IRequestHandler<,>).MakeGenericType(requestType, typeof(TResponse));
        var serviceProviderParam = Expression.Parameter(typeof(IServiceProvider), "serviceProvider");
        var requestParam = Expression.Parameter(typeof(object), "request");
        var cancellationTokenParam = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
        var getRequiredServiceMethod = GetRequiredServiceGenericMethod.MakeGenericMethod(handlerType);

        var getServiceCall = Expression.Call(null, getRequiredServiceMethod, serviceProviderParam);
        var handlerVar = Expression.Variable(handlerType, "handler");
        var assignHandler = Expression.Assign(handlerVar, getServiceCall);

        var handleMethod = handlerType.GetMethod("Handle")
            ?? throw new InvalidOperationException($"Handle method not found on {handlerType.FullName}");

        var callHandle = Expression.Call(
            handlerVar,
            handleMethod,
            Expression.Convert(requestParam, requestType),
            cancellationTokenParam);

        var lambda = Expression.Lambda<Func<IServiceProvider, object, CancellationToken, Task<TResponse>>>(
            Expression.Block(
                new[] { handlerVar },
                assignHandler,
                callHandle),
            serviceProviderParam,
            requestParam,
            cancellationTokenParam);

        return lambda.Compile();
    }

    private static Func<IServiceProvider, object, CancellationToken, Task> BuildVoidRequestInvoker(Type requestType)
    {
        var handlerType = typeof(IRequestHandler<>).MakeGenericType(requestType);
        var serviceProviderParam = Expression.Parameter(typeof(IServiceProvider), "serviceProvider");
        var requestParam = Expression.Parameter(typeof(object), "request");
        var cancellationTokenParam = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
        var getRequiredServiceMethod = GetRequiredServiceGenericMethod.MakeGenericMethod(handlerType);

        var getServiceCall = Expression.Call(null, getRequiredServiceMethod, serviceProviderParam);
        var handlerVar = Expression.Variable(handlerType, "handler");
        var assignHandler = Expression.Assign(handlerVar, getServiceCall);

        var handleMethod = handlerType.GetMethod("Handle")
            ?? throw new InvalidOperationException($"Handle method not found on {handlerType.FullName}");

        var callHandle = Expression.Call(
            handlerVar,
            handleMethod,
            Expression.Convert(requestParam, requestType),
            cancellationTokenParam);

        var lambda = Expression.Lambda<Func<IServiceProvider, object, CancellationToken, Task>>(
            Expression.Block(
                new[] { handlerVar },
                assignHandler,
                callHandle),
            serviceProviderParam,
            requestParam,
            cancellationTokenParam);

        return lambda.Compile();
    }
}
