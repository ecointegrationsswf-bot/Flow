using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Configuration;

namespace AgentFlow.Infrastructure.Storage;

public class AzureBlobStorageService : IBlobStorageService
{
    private readonly string _connStr;
    private readonly BlobContainerClient _container;

    public AzureBlobStorageService(IConfiguration config)
    {
        _connStr   = config["AzureBlobStorage:ConnectionString"] ?? "";
        _container = new BlobContainerClient(_connStr,
            config["AzureBlobStorage:ContainerName"] ?? "agent-documents");
    }

    public async Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default)
    {
        await _container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);
        // Asegurar acceso público aunque el contenedor ya existiera con None
        await _container.SetAccessPolicyAsync(PublicAccessType.Blob, cancellationToken: ct);
        var blob = _container.GetBlobClient(path);
        await blob.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);
        return blob.Uri.ToString();
    }

    /// <summary>
    /// Sube un archivo de media de WhatsApp al container público "whatsapp-media".
    /// Devuelve la URL pública del blob para que UltraMsg pueda servirla al cliente.
    /// </summary>
    public async Task<string> UploadWhatsAppMediaAsync(string filename, byte[] content, string contentType, CancellationToken ct = default)
    {
        var mediaContainer = new BlobContainerClient(_connStr, "whatsapp-media");
        await mediaContainer.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);

        var blob = mediaContainer.GetBlobClient(filename);
        using var ms = new MemoryStream(content);
        await blob.UploadAsync(ms, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);
        return blob.Uri.ToString();
    }

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(path);
        await blob.DeleteIfExistsAsync(cancellationToken: ct);
    }

    public async Task<(Stream Content, string ContentType)> DownloadAsync(string path, CancellationToken ct = default)
    {
        var blob = _container.GetBlobClient(path);
        var response = await blob.DownloadStreamingAsync(cancellationToken: ct);
        var contentType = response.Value.Details.ContentType ?? "application/octet-stream";
        return (response.Value.Content, contentType);
    }

    public async Task<string> UploadToContainerAsync(string containerName, string path, byte[] content, string contentType, CancellationToken ct = default)
    {
        var container = new BlobContainerClient(_connStr, containerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: ct);
        var blob = container.GetBlobClient(path);
        using var ms = new MemoryStream(content);
        await blob.UploadAsync(ms, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);
        return blob.Uri.ToString();
    }

    public async Task<string> UploadAndGetSasUrlAsync(
        string containerName, string path, byte[] content, string contentType,
        TimeSpan validFor, CancellationToken ct = default)
    {
        // Container PRIVADO: sin acceso público. El acceso al blob será solo por SAS.
        var container = new BlobContainerClient(_connStr, containerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var blob = container.GetBlobClient(path);
        using var ms = new MemoryStream(content);
        await blob.UploadAsync(ms, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);

        // Construir SAS de blob solo-lectura, válido por validFor.
        // Tolerancia hacia atrás de 5 min para evitar issues de skew de reloj.
        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = containerName,
            BlobName = path,
            Resource = "b", // blob
            StartsOn = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresOn = DateTimeOffset.UtcNow.Add(validFor),
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        // Si el cliente expone GenerateSasUri, lo usamos directamente (firma con la cuenta).
        if (blob.CanGenerateSasUri)
        {
            return blob.GenerateSasUri(sasBuilder).ToString();
        }

        // Fallback manual: parsear la cuenta+key del connection string y firmar.
        var (account, key) = ParseAccountKey(_connStr);
        var credential = new StorageSharedKeyCredential(account, key);
        var sasToken = sasBuilder.ToSasQueryParameters(credential).ToString();
        return $"{blob.Uri}?{sasToken}";
    }

    private static (string AccountName, string AccountKey) ParseAccountKey(string cs)
    {
        string? account = null, key = null;
        foreach (var part in cs.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx <= 0) continue;
            var k = part[..idx].Trim();
            var v = part[(idx + 1)..];
            if (k.Equals("AccountName", StringComparison.OrdinalIgnoreCase)) account = v;
            else if (k.Equals("AccountKey", StringComparison.OrdinalIgnoreCase)) key = v;
        }
        if (account is null || key is null)
            throw new InvalidOperationException("ConnectionString sin AccountName/AccountKey — no se puede generar SAS.");
        return (account, key);
    }
}
