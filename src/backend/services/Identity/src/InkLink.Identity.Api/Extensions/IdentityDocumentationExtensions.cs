namespace InkLink.Identity.Api.Extensions;

internal static class IdentityDocumentationExtensions
{
    public static IServiceCollection AddIdentityDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddSchemaTransformer<ProblemDetailsSchemaTransformer>();
            options.AddOperationTransformer<AuthenticationOpenApi>();
        });
        return services;
    }

    public static WebApplication MapIdentityDocumentation(this WebApplication app)
    {
        if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")) app.MapOpenApi();
        return app;
    }
}
