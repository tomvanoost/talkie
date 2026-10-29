using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor;
using MudBlazor.Services;
using Talkie.Web.Client;
using Talkie.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
    config.SnackbarConfiguration.VisibleStateDuration = 3500;
    config.SnackbarConfiguration.ShowTransitionDuration = 200;
});

builder.Services.AddSingleton<TokenStore>();
builder.Services.AddSingleton(sp => new HttpClient(new AuthHeaderHandler(sp.GetRequiredService<TokenStore>())
{
    InnerHandler = new HttpClientHandler(),
})
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
});
builder.Services.AddSingleton<ApiClient>();
builder.Services.AddSingleton<SessionState>();
builder.Services.AddSingleton<AuthenticationStateProvider>(sp => sp.GetRequiredService<SessionState>());
builder.Services.AddSingleton<ChatHubClient>();
builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();

await builder.Build().RunAsync();
