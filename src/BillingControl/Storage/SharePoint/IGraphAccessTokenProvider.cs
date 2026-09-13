namespace BillingControl.Storage.SharePoint;

/// <summary>
/// Supplies a Microsoft Graph access token without coupling IDocumentStorage to an
/// identity SDK or credential shape.
/// </summary>
public interface IGraphAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
