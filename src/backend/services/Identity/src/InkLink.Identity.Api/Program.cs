using InkLink.Identity.Application;
using InkLink.Identity.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddIdentityInfrastructure(builder.Configuration);
builder.Services.AddScoped<GetMyProfile>();
builder.Services.AddScoped<ICurrentUser, UnavailableCurrentUser>();
var app = builder.Build();
app.Run();

public partial class Program;

internal sealed class UnavailableCurrentUser : ICurrentUser { public Guid? UserId => null; }
