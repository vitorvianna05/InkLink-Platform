using InkLink.Identity.Application;
using InkLink.Identity.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

if (args.Length != 3 || args[0] != "grant-artist" || !Uri.TryCreate(args[1], UriKind.Absolute, out _) || string.IsNullOrWhiteSpace(args[2]))
{
    Console.Error.WriteLine("Usage: grant-artist <issuer> <subject>. Configure IdentityDatabase__ConnectionString with the administrative credential.");
    return 2;
}
try
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddScoped<ActivateArtist>();
    services.AddIdentityInfrastructure(new ConfigurationBuilder().AddEnvironmentVariables().Build());
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
