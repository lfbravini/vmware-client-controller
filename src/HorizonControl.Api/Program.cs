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
builder.Services.AddHttpClient("horizon-broker", c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddHostedService<BrokerPollerService>();

var app = builder.Build();

var fleetOpts = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<FleetControlOptions>>().Value;
if (app.Environment.IsProduction())
{
    var usingDefaults =
        string.Equals(fleetOpts.AdminApiKey, FleetControlOptions.DefaultAdminApiKey, StringComparison.Ordinal) ||
        string.Equals(fleetOpts.LabEnrollmentToken, FleetControlOptions.DefaultLabEnrollmentToken, StringComparison.Ordinal);
    if (usingDefaults && !fleetOpts.AllowLabDefaults)
    {
        throw new InvalidOperationException(
            "Production refuses default FleetControl AdminApiKey/LabEnrollmentToken. " +
            "Set strong secrets or FleetControl__AllowLabDefaults=true for an explicit lab.");
    }

    if (usingDefaults)
    {
        app.Logger.LogWarning(
            "Production is running with documented lab default secrets (AllowLabDefaults=true). Do not expose this edge to the public Internet.");
    }
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok", version = "v1-lab" }));
app.MapAgentEndpoints();
app.MapAdminEndpoints();
app.MapHub<AgentHub>("/hubs/agent");

// Lab mock Horizon Connection Server surface (polled by BrokerPollerService).
if (fleetOpts.AllowLabDefaults)
{
    app.MapGet("/lab/mock-horizon/rest/monitor/v1/sessions", () =>
        Results.Ok(new
        {
            sessions = new[]
            {
                new
                {
                    id = "lab-session-1",
                    user_name = "lab.user",
                    dns_name = "rdsh-lab-01"
                }
            }
        }));
}

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
