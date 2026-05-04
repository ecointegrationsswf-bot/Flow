namespace AgentFlow.Infrastructure.Campaigns.V2;

/// <summary>
/// Tabla de warm-up para campañas — replica la del workflow n8n
/// (workflow_campana_v3.json, nodo "Filtrar duplicados y aplicar límite diario").
///
/// El "warmup day" es el día consecutivo de uso intenso de la línea WhatsApp.
/// Día 1 = 20 mensajes, día 7+ = 500. Día 0 (sin warm-up) y >7 caen al máximo.
/// </summary>
public static class WarmupLimiter
{
    public const int Default = 500;

    /// <summary>
    /// Devuelve el límite diario aplicable según el día de warm-up.
    /// </summary>
    public static int DailyLimitFor(int warmupDay) => warmupDay switch
    {
        1 => 20,
        2 => 50,
        3 => 100,
        4 => 150,
        5 => 200,
        6 => 300,
        7 => 500,
        _ => Default,   // 0 (sin warm-up) o > 7 (warm-up superado)
    };
}
