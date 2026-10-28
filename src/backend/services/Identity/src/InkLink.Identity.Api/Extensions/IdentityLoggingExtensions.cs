namespace InkLink.Identity.Api.Extensions;

internal static class IdentityLoggingExtensions
{
    public static ILoggingBuilder AddIdentityLogging(this ILoggingBuilder logging)
    {
        logging.ClearProviders();
        logging.AddJsonConsole(options => options.IncludeScopes = true);
        return logging;
    }
}
