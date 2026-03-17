namespace AgentFlow.Infrastructure.Storage;

public interface IBlobStorageService
{
    Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
    Task<(Stream Content, string ContentType)> DownloadAsync(string path, CancellationToken ct = default);
}
