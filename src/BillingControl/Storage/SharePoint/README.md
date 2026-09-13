# Microsoft Graph SharePoint document storage

`GraphSharePointDocumentStorage` implements the frozen `IDocumentStorage` contract
against Microsoft Graph v1.0. It uses explicit resource identifiers and does not
discover a site, drive, or folder from a human-readable path.

Register it from ASP.NET Core when the provider is approved for an environment:

```csharp
builder.Services.AddMicrosoftGraphSharePointDocumentStorage(builder.Configuration);
```

The configuration section is `DocumentStorage:SharePoint`. Environment variables
use the standard double-underscore form:

| Key | Purpose |
| --- | --- |
| `DocumentStorage__SharePoint__GraphBaseUrl` | Graph v1.0 base URL |
| `DocumentStorage__SharePoint__SiteId` | Selected SharePoint site ID |
| `DocumentStorage__SharePoint__DriveId` | Selected document-library drive ID |
| `DocumentStorage__SharePoint__RootFolderItemId` | Pre-provisioned Intake folder item ID |
| `DocumentStorage__SharePoint__MetadataListId` | Document-library list ID |
| `DocumentStorage__SharePoint__MetadataFieldInternalName` | Multiple-lines-of-text field containing the immutable metadata envelope |
| `DocumentStorage__SharePoint__TenantId` | Optional built-in client-credentials tenant setting |
| `DocumentStorage__SharePoint__ClientId` | Optional built-in client-credentials application ID |
| `DocumentStorage__SharePoint__ClientSecret` | Optional server-only client secret; never commit or log it |
| `DocumentStorage__SharePoint__ResumableUploadThresholdBytes` | Switch to upload sessions above this size |
| `DocumentStorage__SharePoint__UploadChunkSizeBytes` | Upload-session fragment size; a 320 KiB multiple below 60 MiB |
| `DocumentStorage__SharePoint__MaxAttempts` | Maximum request attempts |
| `DocumentStorage__SharePoint__RetryBaseDelayMilliseconds` | Exponential-backoff base delay |
| `DocumentStorage__SharePoint__RetryJitterMilliseconds` | Local backoff jitter |
| `DocumentStorage__SharePoint__MaxRetryDelayMilliseconds` | Local backoff ceiling |
| `DocumentStorage__SharePoint__MetadataPayloadMaxBytes` | Maximum serialized metadata field size |

The default registration uses the optional server-side client-credentials token
provider. A certificate, workload identity, or managed-identity deployment can
register another `IGraphAccessTokenProvider` instead. The application must grant
the selected site/library only the least privilege needed for its configured Graph
operations; this phase does not provision tenant permissions or credentials.

The provider derives a non-sensitive stable filename from `LogicalStorageKey`,
enumerates only the configured folder, and sends `@microsoft.graph.conflictBehavior`
`fail` for upload sessions. `DriveId + ItemId` is returned as the canonical
reference. `WebUrl` is informational and the provider has no sharing-link API.
