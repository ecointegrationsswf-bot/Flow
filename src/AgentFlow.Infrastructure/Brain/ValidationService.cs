using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AgentFlow.Domain.Interfaces;
using AgentFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AgentFlow.Infrastructure.Brain;

/// <summary>
/// Gestiona el flujo de validación de identidad (Escenario C).
///
/// Flujo: NotStarted → Questioning → Calling → Validated / Failed
///
/// Lee configuración desde CampaignTemplates.ActionConfigs para la acción VALIDATE_IDENTITY:
/// - validationQuestions: array de preguntas al cliente
/// - webhookUrl, webhookMethod, webhookHeaders: endpoint del tenant
/// - expectedResponseFields: campos esperados en la respuesta del webhook
/// </summary>
public class ValidationService(
    AgentFlowDbContext db,
    IHttpClientFactory httpClientFactory) : IValidationService
{
    private const string ActionName = "VALIDATE_IDENTITY";
    private const int DefaultTimeoutSeconds = 10;

    public async Task<ValidationFlow> StartFlowAsync(
        Guid tenantId, Guid? campaignTemplateId, string intent, CancellationToken ct = default)
    {
        var config = await LoadValidationConfigAsync(tenantId, campaignTemplateId, ct);

        if (config is null || config.Questions.Count == 0)
        {
            // Sin configuración de validación → no se puede validar, continuar normal
            return new ValidationFlow(
                new ValidationContext(intent, [], new Dictionary<string, string>(), DateTime.UtcNow),
                null,
                false,
                true);
        }

        var state = new ValidationContext(
            intent,
            config.Questions.ToList(),
            new Dictionary<string, string>(),
            DateTime.UtcNow);

        // Formular la primera pregunta
        var firstQuestion = state.PendingQuestions[0];

        return new ValidationFlow(state, firstQuestion, false, false);
    }

    public Task<ValidationFlow> ContinueFlowAsync(
        ValidationContext currentState, string clientAnswer, CancellationToken ct = default)
    {
        if (currentState.PendingQuestions.Count == 0)
        {
            return Task.FromResult(new ValidationFlow(currentState, null, true, false));
        }

        // Guardar la respuesta a la pregunta actual
        var currentQuestion = currentState.PendingQuestions[0];
        var updatedAnswers = new Dictionary<string, string>(currentState.CollectedAnswers)
        {
            [currentQuestion] = clientAnswer.Trim()
        };
        var remainingQuestions = currentState.PendingQuestions.Skip(1).ToList();

        var updatedState = currentState with
        {
            PendingQuestions = remainingQuestions,
            CollectedAnswers = updatedAnswers
        };

        // ¿Quedan preguntas?
        if (remainingQuestions.Count > 0)
        {
            return Task.FromResult(new ValidationFlow(
                updatedState,
                remainingQuestions[0],
                false,
                false));
        }

        // Todas las preguntas respondidas → listo para llamar webhook
        return Task.FromResult(new ValidationFlow(updatedState, null, true, false));
    }

    public async Task<ValidationResult> CallWebhookAsync(
        Guid tenantId, Guid? campaignTemplateId, ValidationContext ctx, CancellationToken ct = default)
    {
        var config = await LoadValidationConfigAsync(tenantId, campaignTemplateId, ct);

        if (config is null || string.IsNullOrEmpty(config.WebhookUrl))
        {
            return new ValidationResult(false, "No se pudo validar su identidad. Un ejecutivo lo atenderá.", null);
        }

        try
        {
            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds > 0 ? config.TimeoutSeconds : DefaultTimeoutSeconds);

            var payload = JsonSerializer.Serialize(ctx.CollectedAnswers);

            var request = new HttpRequestMessage(
                config.WebhookMethod?.ToUpper() == "GET" ? HttpMethod.Get : HttpMethod.Post,
                config.WebhookUrl);

            if (request.Method == HttpMethod.Post)
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            // Inyectar headers de autenticación
            if (!string.IsNullOrEmpty(config.WebhookHeaders))
            {
                try
                {
                    var headers = JsonSerializer.Deserialize<Dictionary<string, string>>(config.WebhookHeaders);
                    if (headers is not null)
                    {
                        foreach (var (key, value) in headers)
                            request.Headers.TryAddWithoutValidation(key, value);
                    }
                }
                catch { /* Headers inválidos — continuar sin ellos */ }
            }

            var response = await client.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                return new ValidationResult(false,
                    "No pudimos verificar su identidad en este momento. Un ejecutivo lo atenderá pronto.", null);
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var responseData = new Dictionary<string, string>();

            try
            {
                using var doc = JsonDocument.Parse(responseBody);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    responseData[prop.Name] = prop.Value.ToString();
                }
            }
            catch { /* Respuesta no es JSON — ignorar */ }

            // Verificar campos esperados
            if (config.ExpectedFields.Count > 0)
            {
                var missingFields = config.ExpectedFields
                    .Where(f => !responseData.ContainsKey(f))
                    .ToList();

                if (missingFields.Count > 0)
                {
                    return new ValidationResult(false,
                        "La verificación no fue exitosa. Un ejecutivo lo atenderá.", null);
                }
            }

            return new ValidationResult(true, null, responseData);
        }
        catch (TaskCanceledException)
        {
            return new ValidationResult(false,
                "La verificación tardó demasiado. Un ejecutivo lo atenderá pronto.", null);
        }
        catch
        {
            return new ValidationResult(false,
                "Ocurrió un error al verificar su identidad. Un ejecutivo lo atenderá.", null);
        }
    }

    // ── Config loader ──

    private record ValidateIdentityConfig(
        string? WebhookUrl,
        string? WebhookMethod,
        string? WebhookHeaders,
        List<string> Questions,
        List<string> ExpectedFields,
        int TimeoutSeconds);

    private async Task<ValidateIdentityConfig?> LoadValidationConfigAsync(
        Guid tenantId, Guid? campaignTemplateId, CancellationToken ct)
    {
        // Buscar el ActionDefinitionId de VALIDATE_IDENTITY para este tenant
        var actionId = await db.ActionDefinitions
            .Where(a => a.TenantId == tenantId && a.Name == ActionName && a.IsActive)
            .Select(a => a.Id)
            .FirstOrDefaultAsync(ct);

        if (actionId == Guid.Empty) return null;

        // Si hay campaignTemplateId, buscar la config en ActionConfigs de ese template
        string? actionConfigs = null;
        if (campaignTemplateId.HasValue)
        {
            actionConfigs = await db.CampaignTemplates
                .Where(t => t.Id == campaignTemplateId.Value && t.TenantId == tenantId)
                .Select(t => t.ActionConfigs)
                .FirstOrDefaultAsync(ct);
        }

        // Si no hay config en el template, buscar en cualquier template del tenant que tenga esta acción
        if (string.IsNullOrEmpty(actionConfigs))
        {
            actionConfigs = await db.CampaignTemplates
                .Where(t => t.TenantId == tenantId
                    && t.ActionIds.Contains(actionId)
                    && t.ActionConfigs != null)
                .Select(t => t.ActionConfigs)
                .FirstOrDefaultAsync(ct);
        }

        if (string.IsNullOrEmpty(actionConfigs)) return null;

        return ParseConfig(actionConfigs, actionId);
    }

    private static ValidateIdentityConfig? ParseConfig(string actionConfigs, Guid actionId)
    {
        try
        {
            using var doc = JsonDocument.Parse(actionConfigs);
            var key = actionId.ToString();

            if (!doc.RootElement.TryGetProperty(key, out var cfg))
                return null;

            var webhookUrl = cfg.TryGetProperty("webhookUrl", out var u) ? u.GetString() : null;
            var webhookMethod = cfg.TryGetProperty("webhookMethod", out var m) ? m.GetString() : "POST";
            var webhookHeaders = cfg.TryGetProperty("webhookHeaders", out var h) ? h.GetString() : null;
            var timeout = cfg.TryGetProperty("timeoutSeconds", out var t) && t.TryGetInt32(out var tv) ? tv : DefaultTimeoutSeconds;

            var questions = new List<string>();
            if (cfg.TryGetProperty("validationQuestions", out var vq) && vq.ValueKind == JsonValueKind.Array)
            {
                foreach (var q in vq.EnumerateArray())
                {
                    var text = q.GetString();
                    if (!string.IsNullOrEmpty(text))
                        questions.Add(text);
                }
            }

            var expectedFields = new List<string>();
            if (cfg.TryGetProperty("expectedResponseFields", out var ef) && ef.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in ef.EnumerateArray())
                {
                    var text = f.GetString();
                    if (!string.IsNullOrEmpty(text))
                        expectedFields.Add(text);
                }
            }

            return new ValidateIdentityConfig(webhookUrl, webhookMethod, webhookHeaders, questions, expectedFields, timeout);
        }
        catch
        {
            return null;
        }
    }
}
