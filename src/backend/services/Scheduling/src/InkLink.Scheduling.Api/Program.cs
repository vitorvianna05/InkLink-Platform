using InkLink.Observability;
using InkLink.Scheduling.Api.Extensions;
using InkLink.Scheduling.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddSchedulingLogging();
builder.Services.AddSchedulingInfrastructure(builder.Configuration);
builder.Services.AddSchedulingUseCases();
builder.Services.AddSchedulingAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddIdentityIntegration(builder.Configuration, builder.Environment);
builder.Services.AddSchedulingIntegrations(builder.Configuration, builder.Environment);
builder.Services.AddSchedulingHttpContracts();
builder.Services.AddSchedulingDocumentation();
builder.Services.AddSchedulingOutbox(builder.Configuration, builder.Environment);

builder.Services.AddSchedulingClosure();
builder.Services.AddSchedulingSessions();

var app = builder.Build();
app.UseCorrelatedRequestLogging();
app.UseSchedulingHttpErrors();
app.UseSchedulingAuthentication();
app.MapSchedulingHealth();
app.MapSchedulingDocumentation();
app.MapScheduling();
app.MapSchedulingClosure();
app.MapSchedulingSessions();
app.Run();

public partial class SchedulingProgram;
