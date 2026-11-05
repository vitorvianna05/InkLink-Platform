using System.Text.Json;
using InkLink.Authentication;
using InkLink.Gateway.Documentation;

namespace InkLink.Gateway.Extensions;

internal static class GatewayDocumentationExtensions
{
    public static IServiceCollection AddGatewayDocumentation(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing")) return services;
        var authority = configuration["Authentication:Authority"]?.TrimEnd('/');
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var issuer) || issuer.Scheme != "https")
            throw new InvalidOperationException("Gateway documentation requires an HTTPS Keycloak authority.");
        var documents = new List<GatewayDocument>();
        void Add(string name, string setting, params string[] prefixes)
        {
            var address = configuration[setting];
            if (string.IsNullOrWhiteSpace(address)) return;
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidOperationException("Invalid documentation backend address.");
            documents.Add(new(name, new Uri(uri.ToString().TrimEnd('/') + "/"), prefixes));
        }
        Add("identity", "Gateway:IdentityAddress", "/api/v1/profiles");
        Add("catalog", "Gateway:CatalogAddress", "/api/v1/artists", "/api/v1/favorites", "/api/v2/artists");
        Add("engagements", "Gateway:EngagementsAddress", "/api/v2/engagements");
        Add("messaging", "Gateway:MessagingAddress", "/api/v1/conversations");
        Add("scheduling", "Gateway:SchedulingAddress", "/api/v1/scheduling");
        Add("finance", "Gateway:FinanceAddress", "/api/v1/finance");
        services.AddSingleton(new GatewayDocumentationOptions(authority!, documents));
        services.AddHttpClient<GatewayDocuments>(http => http.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        return services;
    }
    public static WebApplication UseGatewayDocumentation(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing")) return app;
        var settings = app.Services.GetRequiredService<GatewayDocumentationOptions>();
        app.UseSwaggerUI(options =>
        {
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "InkLink — APIs pelo Gateway";
            foreach (var source in settings.Documents) options.SwaggerEndpoint("./documents/" + source.Name + ".json", source.Name);
            options.OAuthClientId("inklink-swagger");
            options.OAuthAppName("InkLink API Explorer");
            options.OAuthScopes("openid", "profile");
            options.OAuthUsePkce();
            options.ConfigObject.PersistAuthorization = false;
            options.ConfigObject.ValidatorUrl = null;
        });
        return app;
    }
    public static IEndpointRouteBuilder MapGatewayDocumentation(this IEndpointRouteBuilder endpoints, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing")) return endpoints;
        endpoints.MapGet("/swagger/documents/{name}.json", async (string name, GatewayDocuments documents, HttpContext context, CancellationToken ct) =>
        {
            if (!documents.Contains(name)) { await ApiErrors.WriteAsync(context, 404, "documentation.not_found"); return; }
            try
            {
                // Relative server keeps every API request on this Gateway origin/path base.
                var document = await documents.ReadAsync(name, context.Request.PathBase.HasValue ? context.Request.PathBase.Value! : "/", ct);
                await context.Response.WriteAsJsonAsync(document, cancellationToken: ct);
            }
            catch (Exception e) when (e is HttpRequestException or JsonException or InvalidDataException or InvalidOperationException
                || e is OperationCanceledException && !ct.IsCancellationRequested)
            { await ApiErrors.WriteAsync(context, 503, "documentation.service_unavailable"); }
        }).AllowAnonymous();
        return endpoints;
    }
}
