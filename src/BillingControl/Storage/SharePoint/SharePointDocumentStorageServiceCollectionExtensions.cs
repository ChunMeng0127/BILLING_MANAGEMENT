using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BillingControl.Storage.SharePoint;

/// <summary>
/// Optional ASP.NET Core registration for the Graph SharePoint storage adapter.
/// </summary>
public static class SharePointDocumentStorageServiceCollectionExtensions
{
    public static IServiceCollection AddMicrosoftGraphSharePointDocumentStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SharePointDocumentStorageOptions>()
            .Bind(configuration.GetSection(SharePointDocumentStorageOptions.SectionName))
            .Validate(options =>
            {
                options.Validate();
                return true;
            }, "Microsoft Graph SharePoint storage configuration is invalid.")
            .ValidateOnStart();

        services.AddHttpClient<GraphClientCredentialsAccessTokenProvider>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                // Graph content downloads may return a pre-authenticated redirect.
                // The provider follows that redirect explicitly without a bearer token.
                AllowAutoRedirect = false
            });
        services.AddSingleton<IGraphAccessTokenProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<GraphClientCredentialsAccessTokenProvider>());

        services.AddHttpClient<GraphSharePointDocumentStorage>(client =>
            {
                // CancellationToken controls request lifetime, including long uploads.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false
            });
        services.AddScoped<IDocumentStorage>(serviceProvider =>
            serviceProvider.GetRequiredService<GraphSharePointDocumentStorage>());

        return services;
    }
}
