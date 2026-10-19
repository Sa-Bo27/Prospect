var builder = DistributedApplication.CreateBuilder(args);

// Shared secret used by n8n to call the /api/automation endpoints.
// Generated on first run and persisted in the AppHost user secrets.
var automationApiKey = builder.AddParameter(
    "automation-api-key",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true);

var cache = builder.AddRedis("cache");

// OpenID Connect identity provider. The "prospect" realm (clients + dev user) is imported
// from ./Realms on first start only: once the volume exists, edit the realm in the admin console.
var keycloak = builder.AddKeycloak("keycloak", port: 8080)
    .WithDataVolume()
    .WithRealmImport("./Realms");

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var prospectDb = postgres.AddDatabase("prospectdb");

var server = builder.AddProject<Projects.Prospect_AI_Server>("server")
    .WithReference(cache)
    .WaitFor(cache)
    .WithReference(prospectDb)
    .WaitFor(prospectDb)
    .WaitFor(keycloak)
    .WithEnvironment("Auth__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/prospect"))
    .WithEnvironment("Automation__ApiKey", automationApiKey)
    // Probed over plain HTTP: the HTTPS dev certificate is not trusted on Linux CI runners.
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithExternalHttpEndpoints();

// n8n handles scheduling and connectors (RSS, IMAP, Telegram) and calls the server API.
// Its own data (workflows, credentials, executions) lives in a SQLite file on the volume.
builder.AddContainer("n8n", "n8nio/n8n", "2.43.7")
    .WithVolume("prospect-n8n-data", "/home/node/.n8n")
    .WithHttpEndpoint(port: 5678, targetPort: 5678)
    .WithHttpHealthCheck("/healthz")
    .WithEnvironment("GENERIC_TIMEZONE", "Europe/Brussels")
    .WithEnvironment("TZ", "Europe/Brussels")
    .WithEnvironment("N8N_BLOCK_ENV_ACCESS_IN_NODE", "false")
    .WithEnvironment("N8N_DIAGNOSTICS_ENABLED", "false")
    .WithEnvironment("PROSPECT_API_URL", server.GetEndpoint("http"))
    .WithEnvironment("PROSPECT_API_KEY", automationApiKey)
    .WaitFor(server);

// Fixed port: it is registered as an allowed redirect URI in the Keycloak realm.
var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithEndpoint("http", endpoint => endpoint.Port = 5173)
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
