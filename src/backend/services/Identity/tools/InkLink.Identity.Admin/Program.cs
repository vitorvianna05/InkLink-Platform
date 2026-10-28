using InkLink.Identity.Admin.Extensions;
using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
return await configuration.RunArtistAdministrationAsync(args);
