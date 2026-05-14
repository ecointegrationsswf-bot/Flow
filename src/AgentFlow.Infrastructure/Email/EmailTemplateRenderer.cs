using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ganss.Xss;
using Scriban;
using Scriban.Runtime;

namespace AgentFlow.Infrastructure.Email;

/// <summary>
/// Renderiza la plantilla HTML del maestro (CampaignTemplate.EmailBodyHtml +
/// EmailSubject) reemplazando variables tipo <c>{{cliente.nombre}}</c> con los
/// datos del contexto y luego sanitiza el HTML para evitar XSS.
///
/// Variables expuestas a la plantilla Scriban:
///   cliente.nombre, cliente.telefono, cliente.email, cliente.poliza,
///   cliente.aseguradora, cliente.saldo,
///   cliente.items[]           ← array genérico (Fase A) — N registros agrupados
///   cliente.items_label       ← etiqueta del array ("Pólizas", "Productos", …)
///   cliente.total_items       ← count del array
///   cliente.is_corporativo    ← true si total_items >= UmbralCorporativo
///   cliente.datos             ← legacy: 1er registro como objeto plano
///   conversacion.{resumen,mensajes,estado}
///   campana.nombre, agente.nombre, tenant.nombre
///   fecha, hora
///
/// Cada item del array expone:
///   item.titulo, item.subtitulo, item.categoria, item.monto
///   item.detalles[]           ← [{ k, v }] para grid de detalle
///   item.raw                  ← todos los campos crudos del CSV
/// </summary>
public class EmailTemplateRenderer
{
    private static readonly HtmlSanitizer _sanitizer = BuildSanitizer();

    private static HtmlSanitizer BuildSanitizer()
    {
        var s = new HtmlSanitizer();
        // Email clients (Gmail/Outlook) suelen requerir style inline y atributos
        // visuales clásicos para que las tablas se rendericen bien.
        s.AllowedSchemes.Add("mailto");
        s.AllowedSchemes.Add("tel");
        // Mantener atributos HTML típicos de templates (width, cellpadding, etc.)
        s.AllowedAttributes.Add("class");
        s.AllowedAttributes.Add("style");
        s.AllowedAttributes.Add("width");
        s.AllowedAttributes.Add("height");
        s.AllowedAttributes.Add("cellpadding");
        s.AllowedAttributes.Add("cellspacing");
        s.AllowedAttributes.Add("align");
        s.AllowedAttributes.Add("valign");
        s.AllowedAttributes.Add("border");
        return s;
    }

    /// <summary>
    /// Renderiza asunto + body. Devuelve un par (subject, bodyHtml) listos para
    /// enviar. textBody se deriva del HTML quitando tags si no se pasa explícito.
    /// </summary>
    public RenderResult Render(
        string? subjectTemplate,
        string? htmlBodyTemplate,
        string? textBodyTemplate,
        EmailRenderContext context)
    {
        var model = BuildScribanModel(context);

        var subject = RenderOne(subjectTemplate, model, "subject");
        var html    = RenderOne(htmlBodyTemplate, model, "body-html");
        var text    = RenderOne(textBodyTemplate, model, "body-text");

        // Sanitizar HTML — siempre, no opcional. Evita que un admin malicioso
        // o un copy/paste con <script> filtre código al correo del cliente.
        if (!string.IsNullOrEmpty(html))
            html = _sanitizer.Sanitize(html);

        // Si no se proporciona textBody, derivar del HTML sanitizado (estandar
        // multipart para clientes que no soportan HTML).
        if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrEmpty(html))
            text = HtmlToPlainText(html);

        return new RenderResult(subject ?? string.Empty, html ?? string.Empty, text);
    }

    private static string? RenderOne(string? template, ScriptObject model, string label)
    {
        if (string.IsNullOrWhiteSpace(template)) return template;

        var parsed = Template.Parse(template);
        if (parsed.HasErrors)
        {
            var errors = string.Join("; ", parsed.Messages);
            Console.WriteLine($"[EmailTemplateRenderer] Template '{label}' tiene errores de sintaxis: {errors}");
            return template;
        }

        try
        {
            var context = new TemplateContext { StrictVariables = false };
            context.PushGlobal(model);
            return parsed.Render(context);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[EmailTemplateRenderer] Falló render de '{label}': {ex.Message}");
            return template;
        }
    }

    /// <summary>
    /// Convierte el modelo .NET en un objeto Scriban consultable como
    /// <c>cliente.nombre</c>, <c>cliente.items[0].titulo</c>, etc.
    /// </summary>
    private static ScriptObject BuildScribanModel(EmailRenderContext ctx)
    {
        var root = new ScriptObject();
        var itemsConfig = ParseItemsConfig(ctx.ItemsConfigJson);
        var rawRecords = ParseContactDataAsArray(ctx.ClienteDatosJson);

        // Si no hay ContactDataJson agrupado, fabricamos un único item desde
        // los campos básicos del contexto (compatibilidad con campañas viejas
        // que no pasaron por FixedFormatCampaignService).
        if (rawRecords.Count == 0)
        {
            rawRecords.Add(BuildLegacyRecord(ctx));
        }

        var items = new ScriptArray();
        foreach (var rec in rawRecords)
        {
            items.Add(BuildItem(rec, itemsConfig));
        }

        var totalItems = items.Count;
        var isCorporativo = totalItems >= Math.Max(1, ctx.UmbralCorporativo);

        // Fallback para cliente.saldo: si ClienteSaldo no vino del CampaignContact
        // (PendingAmount null), sumamos los montos numéricos de los items.
        // Mantiene el formato del primer item (B/. 487.50 → "B/. 702.50").
        var saldoFinal = ctx.ClienteSaldo;
        if (string.IsNullOrWhiteSpace(saldoFinal) && items.Count > 0)
        {
            saldoFinal = ComputeTotalFromItems(items);
        }

        // Detectar datos del ejecutivo asignado mirando las columnas crudas
        // del primer registro (todos los items suelen tener el mismo ejecutivo).
        var ejecutivo = BuildEjecutivoInfo(rawRecords);

        var cliente = new ScriptObject();
        cliente["nombre"]         = ctx.ClienteNombre ?? string.Empty;
        cliente["telefono"]       = ctx.ClienteTelefono ?? string.Empty;
        cliente["email"]          = ctx.ClienteEmail ?? string.Empty;
        cliente["poliza"]         = ctx.ClientePoliza ?? string.Empty;
        cliente["aseguradora"]    = ctx.ClienteAseguradora ?? string.Empty;
        cliente["saldo"]          = saldoFinal ?? string.Empty;
        cliente["ejecutivo"]      = ejecutivo;
        cliente["items"]          = items;
        cliente["items_label"]    = itemsConfig.Label;
        cliente["total_items"]    = totalItems;
        cliente["is_corporativo"] = isCorporativo;
        // Legacy: 1er registro como objeto plano (compat con plantillas viejas).
        cliente["datos"] = rawRecords.Count > 0 ? BuildRawObject(rawRecords[0]) : new ScriptObject();
        root["cliente"] = cliente;

        var conversacion = new ScriptObject();
        conversacion["resumen"]   = ctx.ConversacionResumen ?? string.Empty;
        conversacion["mensajes"]  = ctx.ConversacionMensajesHtml ?? string.Empty;
        conversacion["estado"]    = ctx.ConversacionEstado ?? string.Empty;
        root["conversacion"] = conversacion;

        var campana = new ScriptObject();
        campana["nombre"] = ctx.CampanaNombre ?? string.Empty;
        root["campana"] = campana;
        root["campaña"] = campana;

        var agente = new ScriptObject();
        agente["nombre"] = ctx.AgenteNombre ?? string.Empty;
        root["agente"] = agente;

        var tenant = new ScriptObject();
        tenant["nombre"] = ctx.TenantNombre ?? string.Empty;
        tenant["logo"]   = ctx.TenantLogoUrl ?? string.Empty;
        root["tenant"] = tenant;

        root["fecha"] = ctx.Fecha ?? DateTime.UtcNow.ToString("dd/MM/yyyy");
        root["hora"]  = ctx.Hora  ?? DateTime.UtcNow.ToString("HH:mm");

        return root;
    }

    /// <summary>
    /// Construye un item Scriban con los slots semánticos definidos por el
    /// ItemsConfig. Cada item expone titulo/subtitulo/categoria/monto/detalles
    /// (alimentados por el mapeo) más raw (acceso libre a las columnas crudas).
    /// </summary>
    private static ScriptObject BuildItem(Dictionary<string, string> raw, ItemsConfig cfg)
    {
        var item = new ScriptObject();
        item["titulo"]    = ResolveColumn(raw, cfg.TitleColumn);
        item["subtitulo"] = ResolveColumn(raw, cfg.SubtitleColumn);
        item["categoria"] = ResolveColumn(raw, cfg.CategoryColumn);
        item["monto"]     = ResolveColumn(raw, cfg.AmountColumn);

        var detalles = new ScriptArray();
        foreach (var col in cfg.DetailColumns)
        {
            var v = ResolveColumn(raw, col);
            if (string.IsNullOrWhiteSpace(v)) continue;
            // Saltar valores numéricos en cero — típico de aging buckets sin
            // movimiento ("Saldo A30Dias: 0"). El usuario solo quiere ver los
            // que tienen monto real.
            if (IsNumericZero(v)) continue;
            var entry = new ScriptObject();
            entry["k"] = HumanizeLabel(col);
            entry["v"] = FormatValue(col, v);
            detalles.Add(entry);
        }
        item["detalles"] = detalles;

        // Acceso libre por nombre exacto de columna ({{ item.raw.placa }}).
        item["raw"] = BuildRawObject(raw);
        return item;
    }

    /// <summary>
    /// Extrae los datos del ejecutivo asignado del primer registro. Busca columnas
    /// que mencionen "ejecutivo" y las clasifica por contenido:
    ///   - email      → columna con "email" + "ejecutivo"
    ///   - telefono   → columna con "telefono"/"celular"/"movil" + "ejecutivo"
    ///   - nombre     → columna con "ejecutivo" sin email/telefono
    /// Devuelve un ScriptObject con cliente.ejecutivo.{nombre, email, telefono}.
    /// </summary>
    private static ScriptObject BuildEjecutivoInfo(List<Dictionary<string, string>> records)
    {
        var info = new ScriptObject();
        info["nombre"]   = string.Empty;
        info["email"]    = string.Empty;
        info["telefono"] = string.Empty;
        if (records.Count == 0) return info;

        var first = records[0];
        foreach (var kv in first)
        {
            var key = kv.Key.ToLowerInvariant();
            if (!key.Contains("ejecutivo")) continue;
            var value = kv.Value ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value)) continue;

            var isEmail = key.Contains("email") || key.Contains("correo");
            var isPhone = key.Contains("telefono") || key.Contains("teléfono")
                          || key.Contains("celular") || key.Contains("movil") || key.Contains("móvil");

            if (isEmail && string.IsNullOrEmpty(info["email"] as string))
                info["email"] = value;
            else if (isPhone && string.IsNullOrEmpty(info["telefono"] as string))
                info["telefono"] = value;
            else if (!isEmail && !isPhone && string.IsNullOrEmpty(info["nombre"] as string))
                info["nombre"] = value;
        }
        return info;
    }

    /// <summary>
    /// Convierte un nombre de columna CamelCase/PascalCase a palabras separadas.
    /// Ej: "NumeroDePagos" → "Numero De Pagos", "FechaUltimoPago" → "Fecha Ultimo Pago".
    /// Respeta nombres que ya tienen espacios ("Vigente desde" se queda igual).
    /// Mantiene siglas/abreviaciones consecutivas en mayúsculas (RUC, ID, etc.).
    /// </summary>
    private static string HumanizeLabel(string column)
    {
        if (string.IsNullOrWhiteSpace(column)) return column;
        // Si ya tiene espacios, no cambiamos nada (ya está humanizado).
        if (column.Contains(' ')) return column;

        var sb = new StringBuilder(column.Length + 8);
        for (int i = 0; i < column.Length; i++)
        {
            var c = column[i];
            if (i > 0 && char.IsUpper(c))
            {
                var prev = column[i - 1];
                var next = i + 1 < column.Length ? column[i + 1] : '\0';
                // Insertar espacio si:
                //   - el carácter previo es minúscula ("aB" → "a B")
                //   - el siguiente es minúscula ("ABc" → "AB c" para siglas: "RUCField" → "RUC Field")
                if (char.IsLower(prev) || (char.IsUpper(prev) && char.IsLower(next)))
                {
                    sb.Append(' ');
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// True si el valor representa 0 numérico (con o sin decimales/símbolo
    /// monetario). Acepta "0", "0.00", "0,00", "B/. 0.00", "$0", "-", etc.
    /// </summary>
    private static bool IsNumericZero(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        // Filtrar todo lo que no sea dígito, punto, coma, signo.
        var cleaned = new string(value.Where(c => char.IsDigit(c) || c == '.' || c == ',' || c == '-' || c == '+').ToArray());
        if (string.IsNullOrEmpty(cleaned)) return false;
        // Normalizar coma a punto para parsing invariant.
        cleaned = cleaned.Replace(',', '.');
        if (decimal.TryParse(cleaned, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
        {
            return n == 0m;
        }
        return false;
    }

    /// <summary>
    /// Detecta si un valor parece una fecha y la formatea como dd/MM/yyyy.
    /// Estrategia: descarta cualquier parte de hora (todo después del primer
    /// espacio) y parsea solo la parte fecha contra una lista de formatos comunes.
    /// Robusto a "a. m. / p. m." con espacios non-breaking u otros artefactos
    /// de Excel — esos quedan después del primer espacio y se descartan.
    /// </summary>
    private static string FormatValue(string column, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        // Solo intentar parsear si tiene separadores de fecha — evita convertir
        // números planos ("12") en fechas.
        if (!value.Contains('/') && !value.Contains('-')) return value;
        if (value.Length < 6) return value;

        // Tomar solo la parte fecha — descartar "12:00:00 a. m." y cualquier
        // basura que venga después.
        var datePart = value;
        var firstSpace = value.IndexOf(' ');
        if (firstSpace > 0) datePart = value[..firstSpace];

        var formats = new[]
        {
            "MM/dd/yyyy",   // 04/21/2023 (US)
            "M/d/yyyy",     // 4/21/2023
            "dd/MM/yyyy",   // 21/04/2023 (LATAM)
            "d/M/yyyy",     // 21/4/2023
            "yyyy-MM-dd",   // 2023-04-21 (ISO)
            "yyyy/MM/dd",   // 2023/04/21
        };

        if (DateTime.TryParseExact(
                datePart, formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var parsed))
        {
            return parsed.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }

        return value;
    }

    private static string ResolveColumn(Dictionary<string, string> raw, string? column)
    {
        if (string.IsNullOrWhiteSpace(column)) return string.Empty;
        // Búsqueda case-insensitive — el archivo subido puede traer "Saldo" y
        // el mapeo "saldo".
        foreach (var kv in raw)
            if (string.Equals(kv.Key, column, StringComparison.OrdinalIgnoreCase))
                return kv.Value ?? string.Empty;
        return string.Empty;
    }

    private static ScriptObject BuildRawObject(Dictionary<string, string> raw)
    {
        var obj = new ScriptObject();
        foreach (var kv in raw)
            obj[kv.Key.ToLowerInvariant()] = kv.Value ?? string.Empty;
        return obj;
    }

    /// <summary>
    /// Parsea el ContactDataJson de CampaignContact:
    ///   • Si es un array JSON → cada elemento es un registro (FixedFormat).
    ///   • Si es un objeto JSON → un único registro.
    ///   • Si es null/inválido → array vacío.
    /// </summary>
    private static List<Dictionary<string, string>> ParseContactDataAsArray(string? json)
    {
        var result = new List<Dictionary<string, string>>();
        if (string.IsNullOrWhiteSpace(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    if (elem.ValueKind == JsonValueKind.Object)
                        result.Add(FlattenObject(elem));
                }
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                result.Add(FlattenObject(doc.RootElement));
            }
        }
        catch { /* malformado — ignorar */ }
        return result;
    }

    private static Dictionary<string, string> FlattenObject(JsonElement obj)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in obj.EnumerateObject())
        {
            d[p.Name] = p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => p.Value.TryGetInt64(out var i) ? i.ToString() : p.Value.GetDouble().ToString(),
                JsonValueKind.True or JsonValueKind.False => p.Value.GetBoolean().ToString(),
                JsonValueKind.Null => string.Empty,
                _ => p.Value.ToString(),
            };
        }
        return d;
    }

    /// <summary>
    /// Fabrica un "registro" sintético desde los campos básicos del contexto
    /// cuando no hay ContactDataJson agrupado. Permite que la plantilla
    /// itere igual con 1 elemento.
    /// </summary>
    private static Dictionary<string, string> BuildLegacyRecord(EmailRenderContext ctx)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(ctx.ClientePoliza))      d["poliza"]      = ctx.ClientePoliza;
        if (!string.IsNullOrWhiteSpace(ctx.ClienteAseguradora)) d["aseguradora"] = ctx.ClienteAseguradora;
        if (!string.IsNullOrWhiteSpace(ctx.ClienteSaldo))       d["saldo"]       = ctx.ClienteSaldo;
        return d;
    }

    private static ItemsConfig ParseItemsConfig(string? json)
    {
        var def = new ItemsConfig();
        if (string.IsNullOrWhiteSpace(json)) return def;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return def;
            var root = doc.RootElement;
            if (root.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String)
                def.Label = l.GetString() ?? def.Label;
            if (root.TryGetProperty("titleColumn", out var t) && t.ValueKind == JsonValueKind.String)
                def.TitleColumn = t.GetString();
            if (root.TryGetProperty("subtitleColumn", out var s) && s.ValueKind == JsonValueKind.String)
                def.SubtitleColumn = s.GetString();
            if (root.TryGetProperty("categoryColumn", out var c) && c.ValueKind == JsonValueKind.String)
                def.CategoryColumn = c.GetString();
            if (root.TryGetProperty("amountColumn", out var a) && a.ValueKind == JsonValueKind.String)
                def.AmountColumn = a.GetString();
            if (root.TryGetProperty("detailColumns", out var d) && d.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in d.EnumerateArray())
                    if (elem.ValueKind == JsonValueKind.String)
                        def.DetailColumns.Add(elem.GetString() ?? string.Empty);
            }
        }
        catch { /* malformado — usar defaults */ }
        return def;
    }

    /// <summary>
    /// Suma los <c>monto</c> de los items y devuelve un string con el formato
    /// del primer item (preservando prefijo tipo "B/." o "$"). Útil como fallback
    /// para <c>cliente.saldo</c> cuando el CampaignContact no tiene PendingAmount.
    /// Si los montos no son parseables como número, devuelve el del primer item
    /// tal cual (1 item es el caso más común).
    /// </summary>
    private static string ComputeTotalFromItems(ScriptArray items)
    {
        if (items.Count == 0) return string.Empty;
        string? prefix = null;
        decimal total = 0m;
        var parsedAny = false;
        foreach (var it in items)
        {
            if (it is not ScriptObject so) continue;
            var monto = so["monto"]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(monto)) continue;
            // Detectar prefijo no numérico (ej "B/. 487.50" → "B/. ")
            var trimmed = monto.TrimStart();
            int firstDigit = -1;
            for (int i = 0; i < trimmed.Length; i++)
                if (char.IsDigit(trimmed[i]) || trimmed[i] == '-' || trimmed[i] == '+') { firstDigit = i; break; }
            if (firstDigit < 0) continue;
            prefix ??= trimmed[..firstDigit];
            var numericPart = trimmed[firstDigit..]
                .Replace(",", "", StringComparison.Ordinal)
                .Trim();
            if (decimal.TryParse(numericPart, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var n))
            {
                total += n;
                parsedAny = true;
            }
        }
        if (!parsedAny)
        {
            // No pudimos sumar — devolver el monto del primer item tal cual.
            return items[0] is ScriptObject so ? so["monto"]?.ToString() ?? string.Empty : string.Empty;
        }
        return $"{prefix ?? string.Empty}{total:#,0.00}";
    }

    /// <summary>Conversor mínimo HTML → texto plano (para multipart fallback).</summary>
    private static string HtmlToPlainText(string html)
    {
        if (string.IsNullOrEmpty(html)) return string.Empty;
        var s = html
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</li>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</div>", "\n", StringComparison.OrdinalIgnoreCase);
        var sb = new StringBuilder(s.Length);
        bool inTag = false;
        foreach (var c in s)
        {
            if (c == '<') { inTag = true; continue; }
            if (c == '>') { inTag = false; continue; }
            if (!inTag) sb.Append(c);
        }
        return HtmlEncoder.Default.Encode(sb.ToString()).Trim();
    }

    private sealed class ItemsConfig
    {
        public string Label { get; set; } = "Pólizas";
        public string? TitleColumn    { get; set; }
        public string? SubtitleColumn { get; set; }
        public string? CategoryColumn { get; set; }
        public string? AmountColumn   { get; set; }
        public List<string> DetailColumns { get; set; } = [];
    }
}

/// <summary>Resultado de un render — listo para pasar a IEmailService.SendCustomHtmlAsync.</summary>
public record RenderResult(string Subject, string HtmlBody, string? TextBody);

/// <summary>
/// Datos que la plantilla puede referenciar. Todo opcional — si el caller no
/// los tiene, se rinde como cadena vacía.
/// </summary>
public record EmailRenderContext
{
    public string? ClienteNombre      { get; init; }
    public string? ClienteTelefono    { get; init; }
    public string? ClienteEmail       { get; init; }
    public string? ClientePoliza      { get; init; }
    public string? ClienteAseguradora { get; init; }
    public string? ClienteSaldo       { get; init; }
    /// <summary>JSON arbitrario con campos extra del archivo de campaña.
    /// Puede ser un array (FixedFormat con agrupamiento por teléfono) o un
    /// objeto plano (legacy).</summary>
    public string? ClienteDatosJson   { get; init; }

    public string? ConversacionResumen      { get; init; }
    public string? ConversacionMensajesHtml { get; init; }
    public string? ConversacionEstado       { get; init; }

    public string? CampanaNombre { get; init; }
    public string? AgenteNombre  { get; init; }
    public string? TenantNombre  { get; init; }
    /// <summary>URL del logo del corredor — se incluye en el header del email.</summary>
    public string? TenantLogoUrl { get; init; }

    public string? Fecha { get; init; }
    public string? Hora  { get; init; }

    // ── Fase A: configuración del mapeo de items + umbral corporativo ────
    /// <summary>JSON del CampaignTemplate.ItemsConfig — mapeo de columnas
    /// del archivo a slots semánticos (título, monto, etc.).</summary>
    public string? ItemsConfigJson { get; init; }

    /// <summary>Umbral para considerar al cliente "corporativo".
    /// items.Count >= UmbralCorporativo → cliente.is_corporativo = true.</summary>
    public int UmbralCorporativo { get; init; } = 10;
}
