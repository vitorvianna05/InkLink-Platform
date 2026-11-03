using InkLink.Finance.Application.Ledger;
using InkLink.Finance.Infrastructure.Integration;
namespace InkLink.Finance.Api.Extensions;

internal static class FinanceSourceExtensions
{
    public static IServiceCollection AddFinanceSources(this IServiceCollection services, IConfiguration config, IHostEnvironment environment)
    {
        Uri Address(string key)
        {
            if (!Uri.TryCreate(config[key], UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
                || (uri.Scheme != "https" && !environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
                || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new InvalidOperationException("Finance requires backend URLs; HTTPS outside local/test.");
            return uri;
        }
        services.AddSingleton(new FinanceAddresses(Address("Finance:SchedulingAddress"), Address("Finance:MessagingAddress"), Address("Finance:EngagementsAddress")));
        services.AddHttpClient<IFinanceSources, FinanceSources>(http => http.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, UseCookies = false });
        return services;
    }
}
