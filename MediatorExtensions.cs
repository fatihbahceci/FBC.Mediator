using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Reflection;

namespace FBC.Mediator;

public static class MediatorExtensions
{
    public static IServiceCollection AddMediator(this IServiceCollection services, params Assembly[] assemblies)
    {
        services.AddScoped<IMediator, FBCMediator>();

        var allAssemblies = assemblies.Length > 0 ? assemblies : AppDomain.CurrentDomain.GetAssemblies();

        var handlerTypes = allAssemblies.SelectMany(a => a.GetTypes())
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType &&
                            (i.GetGenericTypeDefinition() == typeof(IRequestHandler<,>) ||
                             i.GetGenericTypeDefinition() == typeof(IRequestHandler<>)))
                .Select(i => new { HandlerType = t, InterfaceType = i }));

        foreach (var handler in handlerTypes)
        {
            services.AddScoped(handler.InterfaceType, handler.HandlerType);
        }

        var endpointTypes = allAssemblies
            .SelectMany(a => a.DefinedTypes)
            .Where(t => typeof(IEndpoint).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);

        foreach (var endpointType in endpointTypes)
        {
            services.AddTransient(endpointType);
            services.AddTransient(typeof(IEndpoint), endpointType);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IPostConfigureOptions<>), typeof(UniversalPostConfigureOptions<>)));

        return services;
    }

    public static IEndpointRouteBuilder UseMediatorEndpoints(this IEndpointRouteBuilder app)
    {
        var endpoints = app.ServiceProvider.GetServices<IEndpoint>();
        foreach (var endpoint in endpoints)
        {
            endpoint.AddRoutes(app);
        }
        return app;
    }
}

internal sealed class UniversalPostConfigureOptions<TOptions> : IPostConfigureOptions<TOptions>
    where TOptions : class
{
    public void PostConfigure(string? name, TOptions options)
    {
        if (options == null)
            return;

        var type = options.GetType();
        if (!type.FullName?.Contains("SwaggerGenOptions", StringComparison.Ordinal) ?? true)
            return;

        try
        {
            var schemaGenOptionsProp = type.GetProperty("SchemaGeneratorOptions",
                BindingFlags.Public | BindingFlags.Instance);
            var schemaGenOptions = schemaGenOptionsProp?.GetValue(options);
            if (schemaGenOptions == null) return;

            var schemaIdSelectorProp = schemaGenOptions.GetType().GetProperty("SchemaIdSelector");
            if (schemaIdSelectorProp == null) return;

            var currentSelector = schemaIdSelectorProp.GetValue(schemaGenOptions) as Func<Type, string>;

            schemaIdSelectorProp.SetValue(schemaGenOptions, (Func<Type, string>)(modelType =>
            {
                if (modelType.DeclaringType != null)
                    return $"{modelType.DeclaringType.Name}_{modelType.Name}";

                return currentSelector?.Invoke(modelType) ?? modelType.Name;
            }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FBC.Mediator: Failed to configure Swagger schema ID selector: {ex.Message}");
        }
    }
}
