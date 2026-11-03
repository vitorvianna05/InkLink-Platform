using InkLink.Engagements.Api.Extensions;
using InkLink.Engagements.Infrastructure;
using InkLink.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddEngagementsLogging();
builder.Services.AddEngagementsInfrastructure(builder.Configuration);
builder.Services.AddEngagementsUseCases();
builder.Services.AddEngagementsAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddIdentityIntegration(builder.Configuration, builder.Environment);
builder.Services.AddCatalogIntegration(builder.Configuration, builder.Environment);
builder.Services.AddEngagementsHttpContracts();
builder.Services.AddEngagementsDocumentation();
if (!builder.Environment.IsEnvironment("Testing") || builder.Configuration.GetValue<bool>("Broker:EnableWorkers")) builder.Services.AddHostedService<OutboxWorker>();

var app = builder.Build();
app.UseCorrelatedRequestLogging();
app.UseEngagementsHttpErrors();
app.UseEngagementsAuthentication();
app.MapEngagementsHealth();
app.MapEngagementsDocumentation();
app.MapNegotiations();
app.Run();

public partial class EngagementsProgram;
