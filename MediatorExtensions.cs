using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
//using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;

namespace FBC.Mediator;

public static class MediatorExtensions
{
    public static IServiceCollection AddMediator(this IServiceCollection services, params Assembly[] assemblies)
    {
        //services.AddSingleton<IMediator, Mediator>();
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
            services.AddTransient(endpointType);                    // with its own type (optional but good)
            services.AddTransient(typeof(IEndpoint), endpointType); // for IEnumerable<IEndpoint> 
        }

        //services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<SwaggerGenOptions>>(new SafeChainedSchemaIdConfigurator()));
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton(typeof(IPostConfigureOptions<>), typeof(UniversalPostConfigureOptions<>)));
        return services;
    }

    public static IApplicationBuilder UseMediatorEndpoints(this IApplicationBuilder app)
    {
        var endpoints = app.ApplicationServices.GetServices<IEndpoint>();
        //var routeBuilder = app.ApplicationServices.GetRequiredService<IEndpointRouteBuilder>();
        var routeBuilder = (IEndpointRouteBuilder)app;
        foreach (var endpoint in endpoints)
        {
            endpoint.AddRoutes(routeBuilder);
        }
        return app;
    }
}
//internal sealed class SafeChainedSchemaIdConfigurator : IPostConfigureOptions<SwaggerGenOptions>
//{
//    public void PostConfigure(string name, SwaggerGenOptions options)
//    {
//        var existingSelector = options.SchemaGeneratorOptions.SchemaIdSelector;

//        options.SchemaGeneratorOptions.SchemaIdSelector = type =>
//        {
//            //// 1. If user has a rule, try it first.
//            //var userResult = existingSelector?.Invoke(type);

//            //// 2. If user returned null or empty string → use our fallback.
//            //if (!string.IsNullOrEmpty(userResult))
//            //    return userResult;

//            // 3. Fallback: like CreateDevice_Command.
//            return type.DeclaringType != null
//                ? $"{type.DeclaringType.Name}_{type.Name}"
//                : type.Name;
//        };
//    }
//}


internal sealed class UniversalPostConfigureOptions<TOptions> : IPostConfigureOptions<TOptions>
    where TOptions : class
{
    public void PostConfigure(string name, TOptions options)
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
        catch
        {
            // swallow
        }
    }
}
