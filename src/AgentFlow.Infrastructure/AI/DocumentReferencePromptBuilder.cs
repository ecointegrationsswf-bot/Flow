using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using AgentFlow.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text;

namespace AgentFlow.Infrastructure.AI;

/// <summary>
/// Implementación del IDocumentReferencePromptBuilder. Lee los documentos desde
/// CampaignTemplateDocuments, construye el bloque textual con las 8 reglas de uso
/// y provee los PDFs en base64 para adjuntarse al request Anthropic.
///
/// La descarga del blob se hace on-demand. En fases futuras se puede cachear el
/// base64 en Redis por TTL corto para reducir egress de Azure.
/// </summary>
public class DocumentReferencePromptBuilder(
    AgentFlowDbContext db,
    IBlobStorageService blobStorage,
    IDocumentRetriever ragRetriever,
    ILogger<DocumentReferencePromptBuilder> log)
    : IDocumentReferencePromptBuilder
{
    // Tope defensivo de PDFs por turno. El AnthropicAgentRunner aplica el mismo
    // límite — duplicado aquí para evitar costos cuando el tenant tiene muchos.
    private const int TenantDocsCap = 5;

    // Timeout duro por documento individual. Si Azure Blob no responde en este
    // tiempo, omitimos ese PDF y seguimos con los demás. Garantiza que un blob
    // colgado nunca trabe el turno completo del agente.
    // (Subido a 30s para alinear con el path activo en AnthropicAgentRunner —
    // antes era 10s y cortaba PDFs de cold blob.)
    private static readonly TimeSpan PerDocumentDownloadTimeout = TimeSpan.FromSeconds(30);

    // Timeout global para descargar TODOS los PDFs del turno. Si se excede,
    // devolvemos lo que hayamos descargado y dejamos que el agente responda
    // con contexto parcial — mejor parcial que nada.
    // (Subido a 90s para soportar 3 PDFs × ~30s de cold blob en peor caso.)
    private static readonly TimeSpan GlobalDownloadTimeout = TimeSpan.FromSeconds(90);

    /// <summary>
    /// Carga TODOS los documentos del tenant ordenados con prioridad al maestro
    /// activo (sus PDFs primero) y luego el resto por fecha de carga descendente.
    /// Esta es la fuente única de verdad para qué documentos se inyectan al
    /// prompt — los tres métodos públicos del builder la consumen.
    /// </summary>
    private async Task<List<TenantDocRecord>> GetOrderedTenantDocsAsync(
        Guid? prioritizeTemplateId, Guid tenantId, CancellationToken ct)
    {
        var raw = await db.CampaignTemplateDocuments
            .Where(d => d.TenantId == tenantId)
            .Select(d => new TenantDocRecord(
                d.Id, d.CampaignTemplateId, d.FileName, d.Description, d.BlobUrl, d.ContentType, d.UploadedAt))
            .ToListAsync(ct);

        return raw
            .OrderByDescending(d => prioritizeTemplateId.HasValue && d.CampaignTemplateId == prioritizeTemplateId.Value)
            .ThenByDescending(d => d.UploadedAt)
            .Take(TenantDocsCap)
            .ToList();
    }

    public async Task<string> BuildTextBlockAsync(
        Guid? prioritizeTemplateId, Guid tenantId, CancellationToken ct = default)
    {
        var docs = await GetOrderedTenantDocsAsync(prioritizeTemplateId, tenantId, ct);
        if (docs.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## DOCUMENTOS DE REFERENCIA DEL TENANT");
        sb.AppendLine();
        sb.AppendLine($"Tienes acceso a {docs.Count} documento(s) PDF de referencia oficial del");
        sb.AppendLine("corredor. Son tu fuente autorizada para responder consultas del cliente");
        sb.AppendLine("sobre productos, coberturas, redes, hospitales, beneficios, ubicaciones,");
        sb.AppendLine("teléfonos, direcciones, plazos y procedimientos. Aplica a TODOS los temas");
        sb.AppendLine("aunque la conversación se haya iniciado en un flujo distinto (cobros,");
        sb.AppendLine("renovaciones, etc.) — los documentos cubren el portafolio completo del");
        sb.AppendLine("corredor, no un único agente.");
        sb.AppendLine();
        sb.AppendLine("### Documentos disponibles:");
        for (int i = 0; i < docs.Count; i++)
        {
            var d = docs[i];
            var desc = string.IsNullOrWhiteSpace(d.Description) ? "(sin descripción)" : d.Description;
            sb.AppendLine($"{i + 1}. {d.FileName} — {desc}");
        }
        sb.AppendLine();
        sb.Append(UsageRules);
        return sb.ToString();
    }

    public async Task<IReadOnlyList<ReferenceDocumentPayload>> GetDocumentsAsBase64Async(
        Guid? prioritizeTemplateId, Guid tenantId, CancellationToken ct = default)
    {
        var docs = await GetOrderedTenantDocsAsync(prioritizeTemplateId, tenantId, ct);
        if (docs.Count == 0) return [];

        // CTS global — si la suma de descargas excede GlobalDownloadTimeout
        // cortamos en seco y devolvemos lo que tengamos. Eso le permite al
        // agente responder con el contexto parcial en vez de quedarse en blanco.
        using var globalCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        globalCts.CancelAfter(GlobalDownloadTimeout);

        var result = new List<ReferenceDocumentPayload>(docs.Count);
        int failed = 0;
        var swTotal = System.Diagnostics.Stopwatch.StartNew();

        foreach (var d in docs)
        {
            if (globalCts.IsCancellationRequested)
            {
                log.LogWarning(
                    "Descarga de docs cortada por timeout global ({Elapsed}ms, descargados={Done}, pendientes={Pending}) tenant={TenantId}",
                    swTotal.ElapsedMilliseconds, result.Count, docs.Count - result.Count - failed, tenantId);
                break;
            }

            using var perDocCts = CancellationTokenSource.CreateLinkedTokenSource(globalCts.Token);
            perDocCts.CancelAfter(PerDocumentDownloadTimeout);
            var swDoc = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var blobPath = ExtractBlobPath(d.BlobUrl);
                var (stream, _) = await blobStorage.DownloadAsync(blobPath, perDocCts.Token);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, perDocCts.Token);
                var base64 = Convert.ToBase64String(ms.ToArray());
                result.Add(new ReferenceDocumentPayload(
                    d.Id, d.FileName, d.Description, base64,
                    string.IsNullOrWhiteSpace(d.ContentType) ? "application/pdf" : d.ContentType));
            }
            catch (OperationCanceledException) when (perDocCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                failed++;
                log.LogWarning(
                    "Documento {DocId} ({FileName}) timeout {Timeout}ms — se omite del turno. tenant={TenantId}",
                    d.Id, d.FileName, (int)PerDocumentDownloadTimeout.TotalMilliseconds, tenantId);
            }
            catch (Exception ex)
            {
                failed++;
                log.LogError(ex,
                    "Falló descarga de documento de referencia {DocId} ({FileName}) en {Elapsed}ms tenant={TenantId}",
                    d.Id, d.FileName, swDoc.ElapsedMilliseconds, tenantId);
            }
        }

        // Si esperabamos documentos pero NINGUNO se descargó, dejamos un log
        // explícito. El agente recibe lista vacía y verá únicamente el bloque
        // textual con la nota "DOC_UNAVAILABLE" agregada por BuildTextBlockAsync.
        if (result.Count == 0 && docs.Count > 0)
        {
            log.LogWarning(
                "Todos los documentos de referencia fallaron ({N} fallidos) tenant={TenantId}. " +
                "El agente responderá sin contexto documental.", failed, tenantId);
        }

        return result;
    }

    public async Task<IReadOnlyList<ReferenceDocument>> GetTenantDocumentsAsync(
        Guid? prioritizeTemplateId, Guid tenantId, CancellationToken ct = default)
    {
        var docs = await GetOrderedTenantDocsAsync(prioritizeTemplateId, tenantId, ct);
        return docs
            .Select(d => new ReferenceDocument(d.FileName, d.BlobUrl, d.Description))
            .ToList();
    }

    public async Task<string> BuildRagContextForQueryAsync(
        Guid? prioritizeTemplateId, Guid tenantId, string clientQuery,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(clientQuery)) return string.Empty;

        // El retriever embed-ea la query, hace cosine sobre los chunks del tenant
        // y devuelve top-K (default 5) con score >= 0.25.
        var chunks = await ragRetriever.RetrieveAsync(
            tenantId, prioritizeTemplateId, clientQuery, topK: 5, minScore: 0.25f, ct);

        if (chunks.Count == 0)
        {
            log.LogInformation(
                "[RAG] tenant={TenantId} query='{Q}' — sin chunks relevantes. Agente responderá sin contexto documental.",
                tenantId, clientQuery.Length > 60 ? clientQuery[..60] : clientQuery);
            return string.Empty;
        }

        // Agrupamos los chunks por documento para que el bloque sea legible y
        // las citas queden ordenadas (página ascendente dentro de cada documento).
        var byDoc = chunks
            .GroupBy(c => new { c.DocumentId, c.FileName, c.Description })
            .Select(g => new
            {
                g.Key.FileName,
                g.Key.Description,
                Pages = g.OrderBy(c => c.PageNumber).ToList()
            })
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## INFORMACIÓN RELEVANTE DE DOCUMENTOS OFICIALES");
        sb.AppendLine();
        sb.AppendLine($"Estos {chunks.Count} fragmentos fueron recuperados automáticamente de los");
        sb.AppendLine("documentos del corredor por su relevancia a la pregunta del cliente.");
        sb.AppendLine("Úsalos como fuente autorizada (productos, coberturas, redes, ubicaciones,");
        sb.AppendLine("teléfonos, plazos, procedimientos). No menciones que son fragmentos al cliente —");
        sb.AppendLine("comunica la información como conocimiento propio del producto.");
        sb.AppendLine();

        foreach (var doc in byDoc)
        {
            var desc = string.IsNullOrWhiteSpace(doc.Description) ? "" : $" — {doc.Description}";
            sb.AppendLine($"### {doc.FileName}{desc}");
            foreach (var c in doc.Pages)
            {
                sb.AppendLine();
                sb.AppendLine($"[Página {c.PageNumber}, relevancia {c.Score:F2}]");
                sb.AppendLine(c.Text);
            }
            sb.AppendLine();
        }

        sb.AppendLine("### REGLAS DE USO");
        sb.AppendLine("1. Si la respuesta del cliente está cubierta por estos fragmentos, ÚSALA con precisión");
        sb.AppendLine("   (nombres, teléfonos, direcciones, plazos textuales — sin parafrasear datos).");
        sb.AppendLine("2. Si los fragmentos NO cubren la pregunta, responde con transparencia y ofrece");
        sb.AppendLine("   escalar a un ejecutivo humano. NO inventes datos no presentes en los fragmentos.");
        sb.AppendLine("3. NO expongas que la información viene de un \"documento\" o \"fragmento\" — el cliente");
        sb.AppendLine("   debe percibirlo como conocimiento natural del agente.");
        sb.AppendLine("4. Para datos PERSONALES del cliente (su póliza, saldo, historial), estos fragmentos");
        sb.AppendLine("   NO los contienen — usa la acción VALIDATE_IDENTITY + consulta correspondiente.");

        return sb.ToString();
    }

    private sealed record TenantDocRecord(
        Guid Id, Guid CampaignTemplateId, string FileName, string? Description,
        string BlobUrl, string? ContentType, DateTime UploadedAt);

    private static string ExtractBlobPath(string blobUrl)
    {
        const string marker = ".blob.core.windows.net/";
        var markerIdx = blobUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIdx < 0) return blobUrl;

        var afterHost = blobUrl[(markerIdx + marker.Length)..];
        var slashIdx = afterHost.IndexOf('/');
        if (slashIdx < 0) return afterHost;

        return Uri.UnescapeDataString(afterHost[(slashIdx + 1)..]);
    }

    /// <summary>
    /// Reglas de uso que acompañan al bloque de documentos. Texto estático,
    /// idéntico en todas las campañas — varía sólo la lista de documentos.
    /// </summary>
    private const string UsageRules = """
### REGLAS DE USO — obligatorias y prioritarias:

0. PROTOCOLO DE BÚSQUEDA EXHAUSTIVA (ANTES DE RESPONDER):
   Cada vez que el cliente haga una pregunta cuyo tema pueda estar
   cubierto por un documento adjunto, DEBES — antes de responder —
   ejecutar mentalmente este protocolo:

   PASO A · Identifica el TEMA de la pregunta del cliente, incluyendo
   los temas implícitos. Una sola palabra del cliente puede activar
   varios temas. Ejemplos:
   - "qué hay cerca en Veraguas" → tema implícito: red de hospitales /
     centros médicos / cobertura geográfica.
   - "se me dañó el carro" → asistencia vehicular / grúa / cobertura.
   - "me pidieron un electrocardiograma" → servicios médicos a
     domicilio / atenciones cubiertas.

   PASO B · Recorre TODA la lista de documentos disponibles (nombre +
   descripción) y todas las secciones internas relevantes (provincias,
   regiones, productos, beneficios, exclusiones, tablas, anexos). NO
   te detengas en la primera sección que parezca no contener la
   respuesta — los PDFs incluyen tablas y secciones diferenciadas; la
   información puede estar en una página posterior.

   PASO C · Solo después de haber recorrido el documento completo
   puedes concluir que la información NO está. Si encontraste la
   respuesta — aunque sea parcial — úsala. Una respuesta parcial pero
   precisa siempre es mejor que declinar.

1. JERARQUÍA DE FUENTES PARA RESPONDER:
   a) Primero las instrucciones del system prompt del agente.
   b) Luego los documentos de referencia adjuntos (búsqueda exhaustiva
      según protocolo del punto 0).
   c) Solo si tras el protocolo no hay nada útil, responde con
      transparencia y ofrece escalar a un ejecutivo humano.

2. PROHIBIDO DECLINAR PREMATURAMENTE.
   NO está permitido responder con frases como "no tengo información
   confiable", "no cuento con esos datos" o "te recomiendo verificar
   directamente" SIN haber recorrido los documentos según el protocolo
   del punto 0. Declinar cuando la respuesta SÍ está en un documento
   adjunto es una falla grave: el cliente percibe al agente como
   inútil y el negocio pierde oportunidad de servicio.

   Ejemplo INCORRECTO (la información sí estaba en el PDF):
   Cliente: "estoy en Veraguas, ¿qué tengo cerca?"
   Agente: "Lamentablemente no tengo información confiable sobre la
   red en Veraguas. Te recomiendo contactar directamente…"

   Ejemplo CORRECTO:
   Cliente: "estoy en Veraguas, ¿qué tengo cerca?"
   Agente: "En Veraguas tienes [hospital A] (tel X), [hospital B]
   (tel Y) y [hospital C] (tel Z). ¿Quieres que te indique el más
   cercano a tu ubicación exacta?"

3. NUNCA INVENTES DATOS NO PRESENTES EN EL DOCUMENTO.
   La precisión y la búsqueda exhaustiva son DOS REGLAS COMPLEMEN-
   TARIAS, no contradictorias. Anti-invención significa: no rellenar
   datos que el documento no tiene. NO significa: declinar cuando el
   documento sí los tiene. Si encuentras el dato exacto en el PDF, úsalo.

4. CITA CON PRECISIÓN. Cuando uses información del documento,
   refiérete al producto, cobertura, hospital, beneficio o cláusula
   por el nombre EXACTO que aparece. Reproduce teléfonos, direcciones,
   montos y plazos textualmente — sin redondear ni parafrasear de
   forma que cambie el sentido. No conviertas monedas a menos que el
   documento mismo lo haga.

5. NO EXPONGAS EL DOCUMENTO COMO FUENTE AL CLIENTE. No digas frases
   como "según el PDF adjunto…", "el documento de referencia indica…"
   o "en los archivos que me pasaron…". El cliente debe percibir tu
   conocimiento como propio del producto. Ejemplos correctos:
   - "La cobertura de accidentes personales incluye…"
   - "En Veraguas la red incluye [Hospital X] al teléfono Y…"
   - "Las condiciones generales establecen un plazo de 30 días para…"

6. NO ENVÍES EL PDF AL CLIENTE DESDE ESTE CONTEXTO. Los documentos de
   referencia son SOLO para tu consulta interna. Si el cliente
   solicita recibir un documento, usa la acción [ACTION:send_document]
   según el protocolo definido en el bloque ACCIONES DISPONIBLES. Los
   PDFs de referencia y los documentos enviables al cliente son
   conceptos distintos.

7. SELECCIÓN INTELIGENTE CUANDO HAY VARIOS DOCUMENTOS. Lee la
   pregunta e identifica TODOS los documentos potencialmente útiles
   por su nombre y descripción. Si la pregunta cruza varios (ej:
   comparar coberturas, listar productos, ubicación + beneficio),
   consulta cada uno y sintetiza una respuesta coherente. Si dos
   documentos se contradicen, prefiere el más específico al caso.
   Cuando solo hay un documento adjunto, considera SIEMPRE que tu
   pregunta puede estar cubierta por él — no descartes su uso por
   inferencia del nombre.

8. LÍMITES DE LOS DOCUMENTOS DE REFERENCIA. Los documentos describen
   productos y procedimientos EN GENERAL. NO contienen datos
   personales del contacto (su póliza individual, su saldo, su
   historial de pagos, su estado de cuenta). Para información
   específica del contacto usa la acción VALIDATE_IDENTITY seguida de
   la acción de consulta correspondiente al webhook del tenant.

9. IDIOMA Y TONO. Responde siempre en el idioma de la conversación
   con el cliente, aunque el documento esté en otro idioma. Adapta el
   tono técnico del documento al tono conversacional definido en tu
   prompt base. Un PDF con lenguaje legal denso no debe traducirse
   literal al chat: extrae la idea y comunícala con claridad.

### ORDEN DE PRECEDENCIA FINAL (de mayor a menor):
1. Instrucciones base del agente (system prompt del Maestro)
2. Contexto dinámico de la sesión (datos del contacto y campaña)
3. Resultados de acciones previas (DataForAgent)
4. Documentos de referencia de esta campaña ← este bloque
5. Conocimiento general del modelo (ÚLTIMO recurso, nunca para datos
   específicos del producto o del cliente)
""";
}
