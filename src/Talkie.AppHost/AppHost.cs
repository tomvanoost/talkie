var builder = DistributedApplication.CreateBuilder(args);

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
    .WaitFor(db);

var api = builder.AddProject<Projects.Talkie_Api>("api")
    .WithReference(db)
    .WithReference(mailpit)
    .WaitFor(db)
    .WaitForCompletion(migrations);

// Hosts the Blazor WebAssembly client and proxies /api and /hubs to the API.
builder.AddProject<Projects.Talkie_Web>("web")
    .WithReference(api)
    .WaitFor(api)
    .WithExternalHttpEndpoints();

builder.Build().Run();
