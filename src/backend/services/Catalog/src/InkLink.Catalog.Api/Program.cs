using InkLink.Catalog.Api.Extensions;
using InkLink.Catalog.Infrastructure;
using InkLink.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddCatalogLogging();
builder.Services.AddCatalogInfrastructure(builder.Configuration);
builder.Services.AddCatalogMedia(builder.Configuration, builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"), builder.Environment.IsEnvironment("Testing"));
builder.Services.AddCatalogUseCases();
builder.Services.AddCatalogAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddEngagementContextAuthorization();
builder.Services.AddIdentityIntegration(builder.Configuration, builder.Environment);
builder.Services.AddProfilePhotoConsumer(builder.Configuration);
builder.Services.AddCatalogHttpContracts();
builder.Services.AddCatalogDocumentation();

var app = builder.Build();
app.UseCorrelatedRequestLogging();
app.UseCatalogHttpErrors();
app.UseCatalogAuthentication();
app.MapCatalogHealth();
app.MapCatalogDocumentation();
app.MapPublicCatalog();
app.MapCatalogPublishing();
app.MapCatalogFavorites();
app.MapEngagementContext();
app.Run();

public partial class CatalogProgram;
