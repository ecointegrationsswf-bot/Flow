namespace AgentFlow.Infrastructure.Storage;

public interface IBlobStorageService
{
    Task<string> UploadAsync(string path, Stream content, string contentType, CancellationToken ct = default);
    Task DeleteAsync(string path, CancellationToken ct = default);
    Task<(Stream Content, string ContentType)> DownloadAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Sube un archivo de media de WhatsApp (imagen, PDF, audio) a un container público.
    /// Devuelve la URL pública del archivo en Azure Blob Storage.
    /// </summary>
    Task<string> UploadWhatsAppMediaAsync(string filename, byte[] content, string contentType, CancellationToken ct = default);
}
