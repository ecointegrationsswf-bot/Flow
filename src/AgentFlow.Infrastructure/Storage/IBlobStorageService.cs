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

    /// <summary>
    /// Sube un archivo a un container ARBITRARIO del mismo storage account. Crea el
    /// container si no existe (acceso público de blob para que los URLs sean compartibles).
    /// Usado por el SendLabelingSummaryExecutor para subir reportes Excel al container "sumary".
    /// </summary>
    Task<string> UploadToContainerAsync(string containerName, string path, byte[] content, string contentType, CancellationToken ct = default);

    /// <summary>
    /// Sube un archivo a un container y devuelve un URL SAS firmado válido por
    /// el TTL indicado (típicamente 2 días para descargas por email). El container
    /// se crea sin acceso público — solo el SAS permite acceder al blob, lo que
    /// evita listados accidentales y limita la ventana de descarga.
    /// </summary>
    Task<string> UploadAndGetSasUrlAsync(
        string containerName, string path, byte[] content, string contentType,
        TimeSpan validFor, CancellationToken ct = default);
}
