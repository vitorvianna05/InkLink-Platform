using InkLink.Identity.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace InkLink.Identity.Admin.Extensions;

internal static class ArtistAdministrationExtensions
{
    public static async Task<int> RunArtistAdministrationAsync(this IConfiguration configuration, string[] args)
    {
        if (args.Length == 1 && args[0] == "list-artist-requests") return await ListRequestsAsync(configuration);
        if (args.Length != 3 || args[0] != "grant-artist" || !Uri.TryCreate(args[1], UriKind.Absolute, out _) || string.IsNullOrWhiteSpace(args[2]))
        {
            Console.Error.WriteLine("Usage: grant-artist <issuer> <subject> | list-artist-requests. Configure IdentityDatabase__ConnectionString with the administrative credential.");
            return 2;
        }
        try
        {
            var services = new ServiceCollection().AddIdentityAdministration(configuration);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var found = await scope.ServiceProvider.GetRequiredService<ActivateArtist>().ExecuteAsync(args[1], args[2], CancellationToken.None);
            Console.WriteLine(found ? "Artist role activated; Client role preserved." : "Account not found. The user must sign in first.");
            return found ? 0 : 3;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Unable to activate artist. Check administrative configuration and database availability.");
            return 1;
        }
    }

    /// <summary>Solicitações de Artist pendentes (decisão 0020), com emissor e sujeito para o grant-artist.</summary>
    private static async Task<int> ListRequestsAsync(IConfiguration configuration)
    {
        try
        {
            var services = new ServiceCollection().AddIdentityAdministration(configuration);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var requests = await scope.ServiceProvider.GetRequiredService<InkLink.Identity.Application.Registration.IArtistRequestReader>().PendingAsync(CancellationToken.None);
            if (requests.Count == 0) Console.WriteLine("No pending artist requests.");
            foreach (var request in requests)
                Console.WriteLine($"{request.RequestedAt:O}	{request.DisplayName}	{request.Issuer}	{request.Subject}");
            return 0;
        }
        catch (Exception)
        {
            Console.Error.WriteLine("Unable to list artist requests. Check administrative configuration and database availability.");
            return 1;
        }
    }
}
