using Aspire.Hosting;

// Ports are configurable so the Playwright suite
// (examples/SqlOS.Todo.E2eTests) can boot this same app host on alternate
// ports with an ephemeral Postgres container while a manually started demo
// keeps running on the defaults. Issuer, audience, and the ASP.NET callback
// all flow from these values.
var builder = DistributedApplication.CreateBuilder(args);

var todoPort = GetPort(builder.Configuration["Todo:ApiPort"], 5080);
var webPort = GetPort(builder.Configuration["Todo:WebPort"], 5090);
var ephemeralSql = string.Equals(
    builder.Configuration["Todo:EphemeralSql"], "true", StringComparison.OrdinalIgnoreCase);
var useSqlServer = string.Equals(
    builder.Configuration["SqlOS:DatabaseProvider"],
    "SqlServer",
    StringComparison.OrdinalIgnoreCase);

var todoOrigin = $"http://localhost:{todoPort}";
var webOrigin = $"http://localhost:{webPort}";
var todoResource = $"{todoOrigin}/api/todos";
var todoIssuer = $"{todoOrigin}/sqlos/auth";
var todoEnableEmailOtp = builder.Configuration["TodoSample:EnableEmailOtp"];
var todoEnableDcr = builder.Configuration["TodoSample:EnableDcr"] ?? "false";
var todoEnablePhoneOtp = builder.Configuration["TodoSample:EnablePhoneOtp"]
    ?? builder.Configuration["SqlOS:PhoneOtp:Enabled"];
var emailConnectionString = builder.Configuration["SqlOS:Email:AzureCommunicationServicesConnectionString"]
    ?? builder.Configuration["SqlOS:EmailOtp:AzureCommunicationServicesConnectionString"]
    ?? builder.Configuration["AZURE_EMAIL_CONNECTION_STRING"];
var emailFromAddress = builder.Configuration["SqlOS:Email:FromAddress"]
    ?? builder.Configuration["SqlOS:EmailOtp:FromAddress"]
    ?? builder.Configuration["AZURE_EMAIL_SENDER_ADDRESS"];
var twilioAccountSid = builder.Configuration["SqlOS:PhoneOtp:TwilioAccountSid"]
    ?? builder.Configuration["TWILIO_ACCOUNT_SID"];
var twilioAuthToken = builder.Configuration["SqlOS:PhoneOtp:TwilioAuthToken"]
    ?? builder.Configuration["TWILIO_AUTH_TOKEN"];
var twilioVerifyServiceSid = builder.Configuration["SqlOS:PhoneOtp:TwilioVerifyServiceSid"]
    ?? builder.Configuration["TWILIO_VERIFY_SERVICE_SID"];
var phoneOtpDefaultRegion = builder.Configuration["SqlOS:PhoneOtp:DefaultRegion"]
    ?? builder.Configuration["TWILIO_DEFAULT_REGION"];

IResourceBuilder<IResourceWithConnectionString> database;
if (useSqlServer)
{
    var sqlPassword = builder.AddParameter("sql-password", value: "LocalDevPassword123!");
    var sql = builder.AddSqlServer("sql", password: sqlPassword, port: 1435)
        .WithContainerRuntimeArgs("--platform", "linux/amd64");
    if (!ephemeralSql)
    {
        sql = sql.WithLifetime(ContainerLifetime.Persistent)
            .WithDataVolume();
    }

    database = sql.AddDatabase("sqlos-todo");
}
else
{
    var postgres = builder.AddPostgres("sql");
    if (!ephemeralSql)
    {
        postgres = postgres.WithLifetime(ContainerLifetime.Persistent)
            .WithDataVolume();
    }

    database = postgres.AddDatabase("sqlos-todo");
}

var todoApi = builder.AddProject<Projects.SqlOS_Todo_Api>("todo-api", launchProfileName: null)
    .WithHttpEndpoint(port: todoPort, isProxied: false)
    .WithReference(database)
    .WaitFor(database)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_URLS", todoOrigin)
    .WithEnvironment("ConnectionStrings__DefaultConnection", database.Resource.ConnectionStringExpression)
    .WithEnvironment("SqlOS__Issuer", todoIssuer)
    .WithEnvironment("TodoSample__PublicOrigin", todoOrigin)
    .WithEnvironment("TodoSample__Resource", todoResource)
    .WithEnvironment("TodoSample__AspNetRedirectUri", $"{webOrigin}/signin-sqlos")
    .WithEnvironment("TodoSample__EnableHeadless", "false")
    .WithEnvironment("TodoSample__EnableDcr", todoEnableDcr)
    .WithEnvironment("SqlOS__DatabaseProvider", useSqlServer ? "SqlServer" : "PostgreSql");

builder.AddProject<Projects.SqlOS_Example_AspNetCoreWeb>("aspnet-web", launchProfileName: null)
    .WithHttpEndpoint(port: webPort, isProxied: false)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_URLS", webOrigin)
    .WithEnvironment("SqlOS__Origin", todoOrigin)
    .WithEnvironment("SqlOS__ClientId", "example-aspnet")
    .WaitFor(todoApi);

if (!string.IsNullOrWhiteSpace(todoEnableEmailOtp))
{
    todoApi.WithEnvironment("TodoSample__EnableEmailOtp", todoEnableEmailOtp);
}

if (!string.IsNullOrWhiteSpace(todoEnablePhoneOtp))
{
    todoApi
        .WithEnvironment("TodoSample__EnablePhoneOtp", todoEnablePhoneOtp)
        .WithEnvironment("SqlOS__PhoneOtp__Enabled", todoEnablePhoneOtp);
}

if (!string.IsNullOrWhiteSpace(emailConnectionString) && !string.IsNullOrWhiteSpace(emailFromAddress))
{
    todoApi
        .WithEnvironment("SqlOS__Email__AzureCommunicationServicesConnectionString", emailConnectionString)
        .WithEnvironment("SqlOS__Email__FromAddress", emailFromAddress)
        .WithEnvironment("SqlOS__EmailOtp__AzureCommunicationServicesConnectionString", emailConnectionString)
        .WithEnvironment("SqlOS__EmailOtp__FromAddress", emailFromAddress);
}

if (!string.IsNullOrWhiteSpace(twilioAccountSid))
{
    todoApi.WithEnvironment("SqlOS__PhoneOtp__TwilioAccountSid", twilioAccountSid);
}

if (!string.IsNullOrWhiteSpace(twilioAuthToken))
{
    todoApi.WithEnvironment("SqlOS__PhoneOtp__TwilioAuthToken", twilioAuthToken);
}

if (!string.IsNullOrWhiteSpace(twilioVerifyServiceSid))
{
    todoApi.WithEnvironment("SqlOS__PhoneOtp__TwilioVerifyServiceSid", twilioVerifyServiceSid);
}

if (!string.IsNullOrWhiteSpace(phoneOtpDefaultRegion))
{
    todoApi.WithEnvironment("SqlOS__PhoneOtp__DefaultRegion", phoneOtpDefaultRegion);
}

builder.Build().Run();

static int GetPort(string? configured, int fallback) =>
    int.TryParse(configured, out var port) ? port : fallback;
