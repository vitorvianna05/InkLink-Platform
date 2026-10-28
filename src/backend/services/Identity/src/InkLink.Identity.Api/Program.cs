using InkLink.Identity.Api.Extensions;
using InkLink.Identity.Infrastructure;
using InkLink.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddIdentityLogging();
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddIdentityUseCases();
builder.Services.AddIdentityAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddIdentityHttpContracts();
builder.Services.AddIdentityDocumentation();

var app = builder.Build();
app.UseCorrelatedRequestLogging();
app.UseIdentityHttpErrors();
app.UseIdentityAuthentication();
app.MapIdentityHealth();
app.MapIdentityDocumentation();
app.MapProfileEndpoints();
app.Run();

public partial class IdentityProgram;
