using InkLink.Messaging.Api.Extensions;
using InkLink.Messaging.Infrastructure;
using InkLink.Observability;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddMessagingLogging();
builder.Services.AddMessagingInfrastructure(builder.Configuration);
builder.Services.AddMessagingUseCases();
builder.Services.AddMessagingAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddIdentityIntegration(builder.Configuration, builder.Environment);
builder.Services.AddConversationPresentation(builder.Configuration, builder.Environment);
builder.Services.AddMessagingHttpContracts();
builder.Services.AddMessagingDocumentation();
builder.Services.AddMessagingTechnicalAuthorization();
builder.Services.AddConversationConsumer(builder.Configuration, builder.Environment);

builder.Services.AddArtistConversationSummary();

var app = builder.Build();
app.UseCorrelatedRequestLogging();
app.UseMessagingHttpErrors();
app.UseMessagingAuthentication();
app.MapMessagingHealth();
app.MapMessagingDocumentation();
app.MapConversations();
app.MapArtistConversationSummary();
app.Run();

public partial class MessagingProgram;
