# FBC.Mediator

`FBC.Mediator` is a lightweight, high-performance **Mediator pattern** implementation for .NET projects.  
It provides **command/query dispatching**, **request handler caching**, and **endpoint registration** for ASP.NET Core.

---

## Features

- `IMediator` for type-safe request/response and void requests
- Supports `IRequestHandler<TRequest, TResponse>` and `IRequestHandler<TRequest>`
- Request handler caching for performance
- Easy integration with ASP.NET Core Minimal APIs
- Compatible with Swagger/OpenAPI
- Auto-registration of handlers and endpoints

---

## Installation

```csharp
// In Startup.cs or Program.cs
builder.Services.AddMediator(typeof(Program).Assembly);
...
var app = builder.Build();
...
app.UseMediatorEndpoints();
...
```

**You can pass multiple assemblies if your handlers or endpoints are spread across projects.**

------

## Usage

### Request & Response

Define a request:

```csharp
public sealed class CreateItem
{
    public record Command(string Name) : IRequest<long>;

    internal sealed class Handler(ILogger<Handler> logger) : IRequestHandler<Command, long>
    {
        public async Task<long> Handle(Command request, CancellationToken token = default)
        {
            logger.LogInformation("Creating item with Name: {ItemName}", request.Name);
            // Perform your logic here
            await Task.Delay(100, token); // simulated async operation
            return new Random().Next(1, 1000); // example ID
        }
    }
}
```

Define an endpoint:

```csharp
public sealed class CreateItemEndPoint : IEndpoint
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/items/create", async (CreateItem.Command command, IMediator mediator, CancellationToken token) =>
        {
            var itemId = await mediator.Send(command, token);
            return Results.Ok(itemId);
        })
        .WithTags("Items")
        .WithName("CreateItem")
        .WithSummary("Creates a new item.")
        .WithDescription("Creates a new item and returns the ID of the created item.")
        .Produces<long>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);
    }
}
```

------

### Void Request

Use a void request:

```csharp
public sealed class VoidTest
{
    public record Command() : IRequest;

    internal sealed class Handler(ILogger<Handler> logger) : IRequestHandler<Command>
    {
        public async Task Handle(Command request, CancellationToken token = default)
        {
            logger.LogInformation("Handling void command");
            await Task.CompletedTask;
        }
    }
}
```

Endpoint for void request:

```csharp
public sealed class VoidTestEndPoint : IEndpoint
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/voidtest", async (VoidTest.Command command, IMediator mediator, CancellationToken token) =>
        {
            await mediator.Send(command, token);
            return Results.Ok();
        })
        .WithTags("Test")
        .WithName("VoidTest")
        .WithSummary("Tests void command.")
        .WithDescription("Sends a void command and returns OK.")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status500InternalServerError);
    }
}
```

------

## DI and Endpoint Integration

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator(typeof(Program).Assembly);

var app = builder.Build();

app.UseMediatorEndpoints();
```

- `AddMediator` automatically registers all handlers and endpoints.
- `UseMediatorEndpoints` maps all `IEndpoint` implementations to the route builder.

------

## Swagger/OpenAPI Support

`FBC.Mediator` includes a safe schema ID selector for Swagger, ensuring nested classes or records display correctly.

------

## Summary

- Type-safe, generic, and high-performance mediator
- Minimal API-friendly
- Handler caching for fast request dispatch
- Supports void and generic requests
- Compatible with Swagger/OpenAPI

------

## Recommended Usage

1. Use `IRequest<TResponse>` for commands and queries.
2. Use `IRequest` for void operations.
3. Define endpoints using `IEndpoint`.
4. Register the mediator using `AddMediator`.
5. Map endpoints using `UseMediatorEndpoints`.
