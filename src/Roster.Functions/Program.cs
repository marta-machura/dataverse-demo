using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.PowerPlatform.Dataverse.Client;
using Roster.Functions;

var builder = FunctionsApplication.CreateBuilder(args);

builder.Services.AddSingleton<IOrganizationServiceAsync2>(sp =>
{
    var url = new Uri(builder.Configuration["DataverseUrl"]
        ?? throw new InvalidOperationException("DataverseUrl app setting is missing."));

    // Managed identity in Azure, your az/VS login locally: no secrets in config.
    var credential = new DefaultAzureCredential();
    var scope = new TokenRequestContext([$"{url.GetLeftPart(UriPartial.Authority)}/.default"]);

    return new ServiceClient(
        url,
        async _ => (await credential.GetTokenAsync(scope)).Token,
        useUniqueInstance: true,
        logger: sp.GetRequiredService<ILogger<ServiceClient>>());
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IVolunteerNotifier, LoggingNotifier>();
builder.Services.AddSingleton<ShiftReminderService>();

builder.Build().Run();
