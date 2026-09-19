# Azure App Service deployment

This application uses Azure App Service, Azure SQL, and private Azure Blob Storage in production. Set configuration through App Service application settings; do not commit secrets or production connection strings.

## Required application settings

```text
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=<Azure SQL connection string>
Dalil__ApiKey=<Gemini API key>
Dalil__Model=<Gemini model>
Storage__BlobServiceUri=https://<storage-account>.blob.core.windows.net
```

`Dalil__ApiKey` is server-side only. If it is absent or Gemini fails, Dalil returns its normal unavailable response and the rest of the application remains available.

## Managed identity and Blob Storage

Enable a managed identity for the App Service and grant it **Storage Blob Data Contributor** on the storage account. The production process uses `DefaultAzureCredential`; do not configure account keys or connection strings for Blob Storage.

Create these private containers, or allow the application identity to create them on first upload:

- `request-evidence`
- `provider-portfolio`

Do not set anonymous public access on either container. Evidence and portfolio content continue to flow through the existing application endpoints, so the current request and portfolio authorization checks remain in control.

## Database deployment

Set `ConnectionStrings__DefaultConnection` in App Service. SQL Server transient failures are retried by the EF Core provider. The application does **not** apply migrations at startup; apply database migrations as a controlled deployment step before or alongside the application rollout.

## Local development

Development continues to use local `App_Data` storage and does not require an Azure account. `App_Data` is development-only, is ignored by Git, and must not be used as persistent production media storage.

For local Gemini configuration, the legacy `Dalil__GeminiApiKey` alias remains supported, but production should use `Dalil__ApiKey`.

## Deployment verification

After deployment, verify that the App Service managed identity can upload and retrieve a request-evidence image and a provider-portfolio image through the application. Confirm both containers remain private and that unauthorised evidence requests still return no media.
