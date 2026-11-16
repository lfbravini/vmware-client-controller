using HorizonControl.Api.Data;
using HorizonControl.Api.Endpoints;
using HorizonControl.Api.Hubs;
using HorizonControl.Api.Options;
using HorizonControl.Api.Security;
using HorizonControl.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FleetControlOptions>(builder.Configuration.GetSection(FleetControlOptions.SectionName));
builder.Services.AddSingleton<EnrollRateLimiter>();
builder.Services.AddSingleton<TenantRlsConnectionInterceptor>();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Host=localhost;Port=5432;Database=horizon_fleet;Username=horizon;Password=horizon_lab";

builder.Services.AddDbContext<FleetDbContext>((sp, options) =>
{
    options.UseNpgsql(connectionString);
    options.AddInterceptors(sp.GetRequiredService<TenantRlsConnectionInterceptor>());
});

builder.Services.AddAuthentication(AgentAuthDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, AgentAuthHandler>(AgentAuthDefaults.Scheme, _ => { });
builder.Services.AddAuthorization();
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Horizon Fleet Control API", Version = "v1" });
    c.AddSecurityDefinition("AdminApiKey", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = AdminApiKeyAttribute.HeaderName,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey
    });
});
builder.Services.AddScoped<DbSeeder>();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapAgentEndpoints();
app.MapAdminEndpoints();
app.MapHub<AgentHub>("/hubs/agent");

using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DbSeeder>();
    try
    {
        await seeder.SeedAsync(CancellationToken.None);
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        logger.LogWarning(ex, "Database seed/migrate skipped or failed (is Postgres up?). API will still start.");
    }
}

app.Run();

public partial class Program;
