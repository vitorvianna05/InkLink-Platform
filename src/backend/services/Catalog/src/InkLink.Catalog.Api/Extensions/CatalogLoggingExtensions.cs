namespace InkLink.Catalog.Api.Extensions;

internal static class CatalogLoggingExtensions
{
    public static ILoggingBuilder AddCatalogLogging(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddJsonConsole(options => options.IncludeScopes = true);
        logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
        logging.AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.Critical);
        logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Critical);
        logging.AddFilter("Microsoft.AspNetCore.Authentication", LogLevel.Critical);
        logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
        return logging;
    }
}
