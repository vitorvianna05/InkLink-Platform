using InkLink.Authentication;
using InkLink.Gateway.Extensions;
using InkLink.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddGatewayLogging();
builder.Services.AddInkLinkAccessTokens(builder.Configuration, builder.Environment);
builder.Services.AddIdentityProxy(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseCorrelatedRequestLogging();
app.UseGatewayHttpPolicies();
app.UseAuthentication();
app.UseAuthorization();
app.MapGatewayHealth();
app.MapReverseProxy();
app.Run();

public partial class GatewayProgram;
