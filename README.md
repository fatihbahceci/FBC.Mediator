# FBC.Mediator

[![NuGet Badge](https://img.shields.io/nuget/v/FBC.Mediator.svg?label=NuGet)](https://www.nuget.org/packages/FBC.Mediator)
[![NuGet Downloads](https://img.shields.io/nuget/dt/FBC.Mediator.svg)](https://www.nuget.org/packages/FBC.Mediator)

---

A lightweight, high-performance Mediator pattern implementation for .NET projects. Provides command/query dispatching, request handler caching, and endpoint registration for ASP.NET Core Minimal APIs. Supports .NET 8, 9 and 10.

## Features

| Feature | Description |
|---------|-------------|
| **IMediator** | Type-safe request/response dispatcher with `Send<TResponse>` and void `Send` |
| **Request Handlers** | `IRequestHandler<TRequest, TResponse>` for typed responses, `IRequestHandler<TRequest>` for void |
| **Handler Caching** | Compiled expression trees cached in `ConcurrentDictionary` for zero-reflection runtime overhead |
| **Endpoint Registration** | `IEndpoint` interface for organizing ASP.NET Core Minimal API routes |
| **Auto DI Registration** | `AddMediator()` scans assemblies and registers handlers + endpoints automatically |
| **Multi-Assembly Support** | Pass multiple assemblies for solutions with separate handler/endpoint projects |
| **Swagger/OpenAPI** | Automatic `SchemaIdSelector` fix for nested types (e.g. `CreateItem.Command`) |

## Installation

```bash
dotnet add package FBC.Mediator
```

## Quick Start

### 1. Define Your Request and Handler

```csharp
public sealed class CreateItem
{
    public record Command(string Name) : IRequest<long>;

    internal sealed class Handler(ILogger<Handler> logger) : IRequestHandler<Command, long>
    {
        public async Task<long> Handle(Command request, CancellationToken token = default)
        {
            logger.LogInformation("Creating item: {Name}", request.Name);
            await Task.Delay(100, token);
            return new Random().Next(1, 1000);
        }
    }
}
```

### 2. Define Your Endpoint

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
        .Produces<long>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest);
    }
}
```

### 3. Register in DI and Map Endpoints

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediator(typeof(Program).Assembly);

var app = builder.Build();

app.UseMediatorEndpoints();

app.Run();
```

---

## Core Concepts

### Request Types

All requests implement a marker interface that tells the mediator what type of response to expect:

```csharp
// Request with response
public record GetUserQuery(int Id) : IRequest<UserDto>;

// Void request (no response)
public record DeleteUserCommand(int Id) : IRequest;
```

The base interface hierarchy:

| Interface | Purpose |
|-----------|---------|
| `IRequestBase` | Root marker interface for all request types |
| `IRequest<out TResponse>` | Request that returns `TResponse` |
| `IRequest` | Request that returns nothing (void) |

### Request Handlers

Each request must have exactly one handler:

```csharp
// Handler for typed response
public class GetUserHandler : IRequestHandler<GetUserQuery, UserDto>
{
    public async Task<UserDto> Handle(GetUserQuery request, CancellationToken token = default)
    {
        // your logic here
    }
}

// Handler for void request
public class DeleteUserHandler : IRequestHandler<DeleteUserCommand>
{
    public async Task Handle(DeleteUserCommand request, CancellationToken token = default)
    {
        // your logic here
    }
}
```

### IMediator

The central dispatcher. Inject it anywhere via DI:

```csharp
public interface IMediator
{
    Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);
    Task Send(IRequest request, CancellationToken cancellationToken = default);
}
```

| Method | Description |
|--------|-------------|
| `Send<TResponse>(request, token)` | Dispatches a request and returns `TResponse` |
| `Send(request, token)` | Dispatches a void request |

### IEndpoint

Interface for defining ASP.NET Core Minimal API endpoints:

```csharp
public interface IEndpoint
{
    void AddRoutes(IEndpointRouteBuilder app);
}
```

Each `IEndpoint` implementation groups related routes together. All implementations are auto-discovered and registered by `AddMediator()`.

---

## Features in Detail

### Nested Request/Handler Pattern

The recommended pattern is to nest the handler inside the request class for clean organization:

```csharp
public sealed class CreateItem
{
    public record Command(string Name) : IRequest<long>;

    internal sealed class Handler(ILogger<Handler> logger) : IRequestHandler<Command, long>
    {
        public async Task<long> Handle(Command request, CancellationToken token = default)
        {
            logger.LogInformation("Creating item: {Name}", request.Name);
            return 42;
        }
    }
}
```

**Key points:**
- Request and handler are co-located for discoverability.
- Handler can be `internal` since the mediator resolves it via DI.
- Swagger automatically renders nested types as `CreateItem_Command`.

### Separate Handler Classes

Handlers do not need to be nested. You can place them in separate files or projects:

```csharp
// In Application layer
public record GetUserById(int Id) : IRequest<UserDto>;

// In a different file or project
public class GetUserByIdHandler(IDbContext db) : IRequestHandler<GetUserById, UserDto>
{
    public async Task<UserDto> Handle(GetUserById request, CancellationToken token = default)
    {
        return await db.Users.FindAsync(request.Id, token);
    }
}
```

### Void Requests

Use `IRequest` for operations that don't return a value:

```csharp
public sealed class SendNotification
{
    public record Command(string Message) : IRequest;

    internal sealed class Handler(ILogger<Handler> logger) : IRequestHandler<Command>
    {
        public async Task Handle(Command request, CancellationToken token = default)
        {
            logger.LogInformation("Sending notification: {Message}", request.Message);
            await Task.CompletedTask;
        }
    }
}
```

Endpoint for a void request:

```csharp
public sealed class SendNotificationEndPoint : IEndpoint
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/notifications/send", async (SendNotification.Command command, IMediator mediator, CancellationToken token) =>
        {
            await mediator.Send(command, token);
            return Results.Ok();
        })
        .WithTags("Notifications")
        .WithName("SendNotification")
        .Produces(StatusCodes.Status200OK);
    }
}
```

### Using IMediator Directly (Without Endpoints)

You can inject `IMediator` into any service, controller, or background job:

```csharp
public class OrderService(IMediator mediator)
{
    public async Task<long> PlaceOrder(string itemName, CancellationToken token)
    {
        var command = new CreateItem.Command(itemName);
        return await mediator.Send(command, token);
    }
}
```

### Multi-Assembly Registration

If your handlers and endpoints live in different projects, pass all assemblies:

```csharp
builder.Services.AddMediator(
    typeof(Program).Assembly,            // Web API project
    typeof(CreateItemHandler).Assembly,  // Application layer
    typeof(UserEndpoints).Assembly       // Endpoints library
);
```

If no assemblies are passed, `AddMediator()` scans all currently loaded assemblies via `AppDomain.CurrentDomain.GetAssemblies()`.

### Handler Caching (Performance)

`FBCMediator` uses a static `ConcurrentDictionary` to cache compiled expression trees:

1. **First request** - Reflection discovers the handler type, builds an `Expression` tree, compiles it to a delegate, and caches the result.
2. **Subsequent requests** - The cached compiled delegate is invoked directly with zero reflection overhead.
3. **Thread-safe** - `ConcurrentDictionary` ensures safe concurrent access.

### Swagger / OpenAPI Support

`FBC.Mediator` automatically configures Swagger's `SchemaIdSelector` so that nested record/class types are displayed correctly:

| Type | Schema ID |
|------|-----------|
| `CreateItem.Command` | `CreateItem_Command` |
| `GetUser.Query` | `GetUser_Query` |

No extra configuration needed - just add Swagger as usual:

```csharp
builder.Services.AddSwaggerGen();
```

### Full Program.cs Example

```csharp
using FBC.Mediator;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMediator(typeof(Program).Assembly);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseMediatorEndpoints();

app.Run();
```

---

## API Reference

### Extension Methods

| Method | Description |
|--------|-------------|
| `AddMediator(params Assembly[] assemblies)` | Registers `IMediator`, all handlers, all endpoints, and Swagger fix into DI |
| `UseMediatorEndpoints()` | Resolves all `IEndpoint` services and calls `AddRoutes()` on each |

### DI Registrations by AddMediator

| Registration | Lifetime | Description |
|---|---|---|
| `IMediator` -> `FBCMediator` | Scoped | The central mediator dispatcher |
| `IRequestHandler<TRequest, TResponse>` | Scoped | All discovered typed request handlers |
| `IRequestHandler<TRequest>` | Scoped | All discovered void request handlers |
| `IEndpoint` implementations | Transient | All discovered endpoint classes (also registered as concrete types) |
| `IPostConfigureOptions<>` | Singleton | Swagger `SchemaIdSelector` fix for nested types |

### Interfaces

| Interface | Purpose |
|-----------|---------|
| `IRequestBase` | Root marker interface for all request types |
| `IRequest<out TResponse>` | Marker for requests returning `TResponse` (covariant) |
| `IRequest` | Marker for void requests |
| `IRequestHandler<in TRequest, TResponse>` | Handler for requests with a response |
| `IRequestHandler<in TRequest>` | Handler for void requests |
| `IMediator` | Central request dispatcher |
| `IEndpoint` | Minimal API endpoint definition |

## Requirements

- .NET 8.0, 9.0, or 10.0
- ASP.NET Core (via `Microsoft.AspNetCore.App` framework reference)

## License

MIT
