using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
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
}
