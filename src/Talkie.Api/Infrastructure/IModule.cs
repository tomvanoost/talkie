namespace Talkie.Api.Infrastructure;

/// <summary>
/// A vertical slice of the modular monolith. Each module registers its own services and maps its own endpoints.
/// </summary>
public interface IModule
{
    void Register(IServiceCollection services, IConfiguration configuration) { }
    void MapEndpoints(IEndpointRouteBuilder app);
}

public static class ModuleExtensions
{
    private static readonly IReadOnlyList<IModule> Modules = typeof(IModule).Assembly.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IModule).IsAssignableFrom(t))
        .Select(t => (IModule)Activator.CreateInstance(t)!)
        .ToList();

    public static IServiceCollection AddModules(this IServiceCollection services, IConfiguration configuration)
    {
        foreach (var module in Modules) module.Register(services, configuration);
        return services;
    }

    public static IEndpointRouteBuilder MapModules(this IEndpointRouteBuilder app)
    {
        foreach (var module in Modules) module.MapEndpoints(app);
        return app;
    }
}
