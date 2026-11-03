using InkLink.Messaging.Api.Extensions;
using InkLink.Messaging.Application.Accounts;
using InkLink.Messaging.Application.Conversations;
using InkLink.Messaging.Infrastructure;
using InkLink.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddMessagingLogging();
builder.Services.AddMessagingInfrastructure(builder.Configuration);
builder.Services.AddScoped<ResolveMessagingAccount>();
builder.Services.AddScoped<ManageConversations>();
builder.Services.AddScoped<OpenConversation>();
builder.Services.AddMessagingAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddIdentityIntegration(builder.Configuration, builder.Environment);
builder.Services.AddMessagingHttpContracts();
builder.Services.AddMessagingDocumentation();
builder.Services.AddMessagingTechnicalAuthorization();
builder.Services.AddConversationConsumer(builder.Configuration, builder.Environment);

var app = builder.Build();
app.UseCorrelatedRequestLogging();
app.UseMessagingHttpErrors();
app.UseMessagingAuthentication();
app.MapMessagingHealth();
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing")) app.MapOpenApi();
app.MapConversations();
app.Run();

public partial class MessagingProgram;
