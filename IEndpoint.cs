using Microsoft.AspNetCore.Routing;

namespace FBC.Mediator;

public interface IEndpoint
{
    void AddRoutes(IEndpointRouteBuilder app);
}
