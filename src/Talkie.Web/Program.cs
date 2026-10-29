using Yarp.ReverseProxy.Configuration;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// The browser only talks to this host. /api and /hubs are forwarded to the API,
// which Aspire service discovery resolves as "https+http://api".
var apiAddress = builder.Configuration["ApiAddress"] ?? "https+http://api";
builder.Services.AddReverseProxy()
    .LoadFromMemory(
        [
            new RouteConfig { RouteId = "api", ClusterId = "api", Match = new RouteMatch { Path = "/api/{**catch-all}" } },
            new RouteConfig { RouteId = "hubs", ClusterId = "api", Match = new RouteMatch { Path = "/hubs/{**catch-all}" } },
        ],
        [
            new ClusterConfig
            {
                ClusterId = "api",
                Destinations = new Dictionary<string, DestinationConfig> { ["api"] = new() { Address = apiAddress } },
            },
        ])
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();
app.UseWebSockets();
app.MapDefaultEndpoints();
app.MapReverseProxy();
app.MapFallbackToFile("index.html");

app.Run();
