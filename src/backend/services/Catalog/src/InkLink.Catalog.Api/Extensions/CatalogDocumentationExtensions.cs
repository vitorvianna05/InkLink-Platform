namespace InkLink.Catalog.Api.Extensions;

internal static class CatalogDocumentationExtensions
{
    public static IServiceCollection AddCatalogDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddSchemaTransformer<ProblemDetailsSchemaTransformer>();
            options.AddSchemaTransformer<CatalogInputSchemaTransformer>();
            options.AddOperationTransformer<AuthenticationOpenApi>();
        });
        return services;
    }

    public static WebApplication MapCatalogDocumentation(this WebApplication app)
    {
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")) app.MapOpenApi();
        return app;
    }
}
