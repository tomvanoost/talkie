using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Talkie.Api.Infrastructure;
using Talkie.Data;
using Talkie.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// ---------- Data (SQL Server 2025, compatibility level 170 for the native vector type) ----------
builder.Services.AddDbContext<TalkieDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("talkiedb")
            ?? throw new InvalidOperationException("Connection string 'talkiedb' is missing."),
        sql => sql.UseCompatibilityLevel(170)));
builder.EnrichSqlServerDbContext<TalkieDbContext>(settings => settings.DisableRetry = true);

// ---------- Auth ----------
var authOptions = builder.Configuration.GetSection("Auth").Get<AuthOptions>() ?? new AuthOptions();
builder.Services.AddSingleton(authOptions);
builder.Services.AddSingleton<TokenService>();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = authOptions.Issuer,
            ValidAudience = authOptions.Audience,
            IssuerSigningKey = authOptions.GetKey(),
            NameClaimType = "sub",
        };
        // SignalR sends the token in the query string for WebSockets.
        o.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    ctx.Token = token;
                }
                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization();

// ---------- Infrastructure ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<CurrentUser>();
builder.Services.AddScoped<DeletionService>();
builder.Services.AddSingleton<EmailSender>();
builder.Services.AddSingleton<ChatNotifier>();
builder.Services.AddSignalR();
builder.Services.AddSingleton<Microsoft.AspNetCore.SignalR.IUserIdProvider, SubjectUserIdProvider>();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = TalkieConstants.MaxAttachmentBytes + 1024 * 1024);

builder.Services.AddModules(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();
app.MapModules();
app.MapHub<ChatHub>("/hubs/chat");

app.Run();

/// <summary>SignalR user id = the JWT "sub" claim, so notifications can target users.</summary>
internal sealed class SubjectUserIdProvider : Microsoft.AspNetCore.SignalR.IUserIdProvider
{
    public string? GetUserId(Microsoft.AspNetCore.SignalR.HubConnectionContext connection) =>
        connection.User?.FindFirst("sub")?.Value;
}

public partial class Program;
