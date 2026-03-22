namespace FBC.Mediator;

public interface IRequestBase;
public interface IRequest<out TResponse> : IRequestBase;
public interface IRequest : IRequestBase;


public interface IRequestHandler<in TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    Task<TResponse> Handle(TRequest request, CancellationToken token = default);
}
public interface IRequestHandler<in TRequest>
    where TRequest : IRequest
{
    Task Handle(TRequest request, CancellationToken token = default);
}