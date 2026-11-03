using InkLink.Catalog.Api.Extensions;
using InkLink.Catalog.Infrastructure;
using InkLink.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddCatalogLogging();
builder.Services.AddCatalogInfrastructure(builder.Configuration);
builder.Services.AddCatalogUseCases();
builder.Services.AddCatalogAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddIdentityIntegration(builder.Configuration, builder.Environment);
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
app.Run();

public partial class CatalogProgram;
