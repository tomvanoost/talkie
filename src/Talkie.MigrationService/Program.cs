using Microsoft.EntityFrameworkCore;
using Talkie.Data;
using Talkie.MigrationService;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

builder.Services.AddDbContext<TalkieDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("talkiedb")
            ?? throw new InvalidOperationException("Connection string 'talkiedb' is missing."),
        sql =>
        {
            sql.UseCompatibilityLevel(170);
            sql.MigrationsAssembly(typeof(TalkieDbContext).Assembly.GetName().Name);
        }));
builder.EnrichSqlServerDbContext<TalkieDbContext>(settings => settings.DisableRetry = true);

builder.Services.AddHostedService<MigrationWorker>();

builder.Build().Run();
