using InkLink.Catalog.Api.Extensions;
using InkLink.Catalog.Infrastructure;
using InkLink.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddCatalogLogging();
builder.Services.AddCatalogInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseCorrelatedRequestLogging();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapCatalogHealth();
app.Run();

public partial class CatalogProgram;
