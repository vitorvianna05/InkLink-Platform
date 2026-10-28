using InkLink.Bff;
using InkLink.Bff.Extensions;
using InkLink.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddBffLogging();
var settings = BffSettings.Load(builder.Configuration, builder.Environment);
builder.Services.AddBffPersistence(settings);
builder.Services.AddBffAuthentication(settings);
builder.Services.AddBffAntiforgery();
builder.Services.AddGatewayProxy(settings);

var app = builder.Build();
app.UseCorrelatedRequestLogging();
app.UseBffHttpPolicies();
app.UseAuthentication();
app.UseAuthorization();
app.UseBffAntiforgery();
app.MapBffHealth();
app.MapBffSessionEndpoints();
app.MapReverseProxy();
app.Run();

public partial class BffProgram;
