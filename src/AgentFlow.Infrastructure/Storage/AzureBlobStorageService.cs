using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;

namespace AgentFlow.Infrastructure.Storage;

public class AzureBlobStorageService(IConfiguration config) : IBlobStorageService
{
    private readonly BlobContainerClient _container = new(
        config["AzureBlobStorage:ConnectionString"],
        config["AzureBlobStorage:ContainerName"] ?? "agent-documents");

    public async Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default)
    {
        await _container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);

        var blob = _container.GetBlobClient(path);
        await blob.UploadAsync(content, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: ct);

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
