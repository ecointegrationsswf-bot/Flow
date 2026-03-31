namespace AgentFlow.Domain.Interfaces;

/// <summary>
/// Servicio de transcripción de audio a texto.
/// Implementado con OpenAI Whisper — mejor soporte para español panameño.
/// </summary>
public interface ITranscriptionService
{
    /// <summary>
    /// Transcribe un archivo de audio (OGG, MP3, M4A, WAV) a texto.
    /// Devuelve null si el servicio no está configurado o falla.
    /// </summary>
    Task<string?> TranscribeAsync(byte[] audioBytes, string fileName, CancellationToken ct = default);
}
