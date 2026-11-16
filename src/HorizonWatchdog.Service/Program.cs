using HorizonWatchdog.Service;
using HorizonWatchdog.Service.ControlPlane;
using HorizonWatchdog.Service.Identity;
using HorizonWatchdog.Service.Ipc;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = "HorizonWatchdog");
builder.Services.AddSingleton<AgentIdentityStore>();
builder.Services.AddSingleton<ControlPlaneClient>();
builder.Services.AddSingleton<NamedPipeServer>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();
