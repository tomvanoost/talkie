using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

// Secrets: a generated (and persisted) JWT signing key, and the password seeded for admin@talkie.com.
var signingKey = builder.AddParameter("auth-signing-key", new GenerateParameterDefault { MinLength = 48, Special = false }, secret: true, persist: true);
var adminPassword = builder.AddParameter("admin-password", "TalkieAdmin1!", secret: true);

// SQL Server 2025 (native vector type) with a persistent data volume.
var sql = builder.AddSqlServer("sql")
    .WithImageTag("2025-latest")
    .WithDataVolume("talkie-sql-data")
    .WithLifetime(ContainerLifetime.Persistent);

var db = sql.AddDatabase("talkiedb");

// MailPit catches every e-mail Talkie sends (welcome mails, temporary passwords).
var mailpit = builder.AddMailPit("mailpit");

// Applies EF Core migrations + seed data, then exits.
var migrations = builder.AddProject<Projects.Talkie_MigrationService>("migrations")
    .WithReference(db)
    .WithEnvironment("Seed__AdminPassword", adminPassword)
    .WaitFor(db);

var api = builder.AddProject<Projects.Talkie_Api>("api")
    .WithReference(db)
    .WithReference(mailpit)
    .WithEnvironment("Auth__SigningKey", signingKey)
    .WaitFor(db)
    .WaitForCompletion(migrations);

// Hosts the Blazor WebAssembly client and proxies /api and /hubs to the API.
builder.AddProject<Projects.Talkie_Web>("web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
