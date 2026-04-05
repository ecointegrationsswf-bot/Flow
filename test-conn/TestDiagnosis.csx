#!/usr/bin/env dotnet-script
#r "nuget: Microsoft.Data.SqlClient, 6.0.1"

using Microsoft.Data.SqlClient;
using System.Net.Http;
using System.Text;
using System.Text.Json;

var API_BASE = "http://jamconsulting-004-site12.site4future.com/api";
var N8N_KEY  = "talkia-n8n-2026";
var CONN_STR = "Server=tcp:sql1003.site4now.net,1433;Database=db_ab2fbb_flow;User Id=db_ab2fbb_flow_admin;Password=u0hwjTvMfyFMVxn6x4YM;TrustServerCertificate=True;Encrypt=True;Connection Timeout=30;MultipleActiveResultSets=True;";

Directory.CreateDirectory("C:/TalkIA/logs");
var LOG_FILE = $"C:/TalkIA/logs/diagnosis-{DateTime.Now:yyyyMMdd-HHmmss}.log";

Action<string> Log = msg => {
    var line = $"[{DateTime.Now:HH:mm:ss.fff}] {msg}";
    Console.WriteLine(line);
    File.AppendAllText(LOG_FILE, line + "\n");
};

await RunDiagnosis();

async Task RunDiagnosis() {
    Log("======================================================");
    Log("  DIAGNÓSTICO CAMPAIGN SEND");
    Log($"  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    Log("======================================================");

    var conn = new SqlConnection(CONN_STR);
    conn.Open();
    Log("BD conectada OK");

    // ── PASO 1: Última campaña ──────────────────────────────
    Log("\n=== PASO 1: Campaña más reciente ===");
    string campaignId="", campaignName="", tenantId="", agentId="", templateId="", status="";
    int total=0, processed=0, delaySecs=10;
    string phone="", clientName="", tenantInstanceId="", tenantToken="", llmApiKey="";

    var cmd1 = new SqlCommand(@"
        SELECT TOP 1 c.Id, c.Name, c.TenantId, c.Status, c.TotalContacts, c.ProcessedContacts,
            c.AgentDefinitionId, c.CampaignTemplateId,
            t.WhatsAppInstanceId, t.WhatsAppApiToken, t.LlmApiKey, t.CampaignMessageDelaySeconds,
            (SELECT TOP 1 cc.PhoneNumber FROM CampaignContacts cc WHERE cc.CampaignId=c.Id AND cc.IsPhoneValid=1 ORDER BY cc.CreatedAt) AS Phone,
            (SELECT TOP 1 cc.ClientName  FROM CampaignContacts cc WHERE cc.CampaignId=c.Id AND cc.IsPhoneValid=1 ORDER BY cc.CreatedAt) AS ClientName
        FROM Campaigns c JOIN Tenants t ON t.Id=c.TenantId
        ORDER BY c.CreatedAt DESC", conn);
    var r1 = cmd1.ExecuteReader();
    r1.Read();
    campaignId       = r1["Id"].ToString()!;
    campaignName     = r1["Name"].ToString()!;
    tenantId         = r1["TenantId"].ToString()!;
    status           = r1["Status"].ToString()!;
    total            = Convert.ToInt32(r1["TotalContacts"]);
    processed        = Convert.ToInt32(r1["ProcessedContacts"]);
    agentId          = r1["AgentDefinitionId"].ToString()!;
    templateId       = r1["CampaignTemplateId"]?.ToString() ?? "";
    tenantInstanceId = r1["WhatsAppInstanceId"] is DBNull ? "" : r1["WhatsAppInstanceId"].ToString()!;
    tenantToken      = r1["WhatsAppApiToken"]   is DBNull ? "" : r1["WhatsAppApiToken"].ToString()!;
    llmApiKey        = r1["LlmApiKey"]           is DBNull ? "" : r1["LlmApiKey"].ToString()!;
    delaySecs        = r1["CampaignMessageDelaySeconds"] is DBNull ? 10 : Convert.ToInt32(r1["CampaignMessageDelaySeconds"]);
    phone            = r1["Phone"]?.ToString() ?? "";
    clientName       = r1["ClientName"]?.ToString() ?? "";
    r1.Close();

    Log($"Campaña    : {campaignName}");
    Log($"ID         : {campaignId}");
    Log($"Estado     : {status}  |  Progreso: {processed}/{total}");
    Log($"LlmApiKey  : {(string.IsNullOrEmpty(llmApiKey) ? "NULL ← PROBLEMA!" : llmApiKey[..Math.Min(20,llmApiKey.Length)] + "...")}");
    Log($"InstanceId (Tenant): {(string.IsNullOrEmpty(tenantInstanceId) ? "NULL" : tenantInstanceId)}");
    Log($"Token      (Tenant): {(string.IsNullOrEmpty(tenantToken) ? "NULL" : tenantToken[..Math.Min(8,tenantToken.Length)] + "...")}");
    Log($"Delay      : {delaySecs}s");
    Log($"SamplePhone: {phone}  |  {clientName}");

    // ── PASO 2: WhatsAppLines ────────────────────────────────
    Log("\n=== PASO 2: WhatsAppLines activas del tenant ===");
    string lineInstanceId = "", lineToken = "";
    var cmd2 = new SqlCommand($"SELECT TOP 1 InstanceId, ApiToken FROM WhatsAppLines WHERE TenantId='{tenantId}' AND IsActive=1 ORDER BY CreatedAt DESC", conn);
    var r2 = cmd2.ExecuteReader();
    if (r2.Read()) {
        lineInstanceId = r2["InstanceId"]?.ToString() ?? "";
        lineToken      = r2["ApiToken"]?.ToString() ?? "";
        Log($"  InstanceId: {lineInstanceId}  Token: {lineToken[..Math.Min(8,lineToken.Length)]}...");
    } else {
        Log("  NO hay WhatsAppLines activas ← PROBLEMA GRAVE");
    }
    r2.Close();

    var effectiveInstanceId = !string.IsNullOrEmpty(tenantInstanceId) ? tenantInstanceId : lineInstanceId;
    var effectiveToken      = !string.IsNullOrEmpty(tenantToken) ? tenantToken : lineToken;
    Log($"\nCredenciales efectivas para n8n:");
    Log($"  InstanceId: {(string.IsNullOrEmpty(effectiveInstanceId) ? "VACÍO ← NO PUEDE ENVIAR" : effectiveInstanceId)}");
    Log($"  Token     : {(string.IsNullOrEmpty(effectiveToken) ? "VACÍO ← NO PUEDE ENVIAR" : effectiveToken[..Math.Min(8,effectiveToken.Length)] + "...")}");

    // ── PASO 3: DispatchStatus contactos ──────────────────────
    Log("\n=== PASO 3: Estado de contactos ===");
    var cmd3 = new SqlCommand($"SELECT DispatchStatus, COUNT(*) AS Cnt FROM CampaignContacts WHERE CampaignId='{campaignId}' GROUP BY DispatchStatus", conn);
    var r3 = cmd3.ExecuteReader();
    while (r3.Read()) Log($"  {r3["DispatchStatus"],-15}: {r3["Cnt"]}");
    r3.Close();

    var cmd3b = new SqlCommand($"SELECT TOP 5 PhoneNumber, IsPhoneValid, DispatchStatus, DispatchError FROM CampaignContacts WHERE CampaignId='{campaignId}'", conn);
    var r3b = cmd3b.ExecuteReader();
    Log("  Muestra de contactos:");
    while (r3b.Read()) {
        var err = r3b["DispatchError"] is DBNull ? "-" : r3b["DispatchError"].ToString()!;
        Log($"    {r3b["PhoneNumber"],-18} valid={r3b["IsPhoneValid"]} status={r3b["DispatchStatus"],-10} err={err[..Math.Min(50,err.Length)]}");
    }
    r3b.Close();

    // ── PASO 4: Prompt del template ──────────────────────────
    Log("\n=== PASO 4: CampaignTemplate + Prompt ===");
    var cmd4 = new SqlCommand($"SELECT ct.Name, ct.PromptTemplateIds FROM CampaignTemplates ct WHERE ct.Id='{templateId}'", conn);
    var r4 = cmd4.ExecuteReader();
    if (r4.Read()) {
        var tplName   = r4["Name"].ToString();
        var promptIds = r4["PromptTemplateIds"]?.ToString() ?? "NULL";
        Log($"  Template : {tplName}  PromptIds: {promptIds}");
        bool hasPrompt = promptIds != "NULL" && promptIds.Length > 10;
        Log($"  Prompt vinculado: {(hasPrompt ? "SÍ" : "NO ← PROBLEMA")}");
    } else {
        Log($"  Template {templateId} NO encontrado");
    }
    r4.Close();

    // ── PASO 5: TEST campaign-send ────────────────────────────
    Log("\n=== PASO 5: TEST directo POST /api/webhooks/campaign-send ===");
    if (string.IsNullOrEmpty(effectiveInstanceId) || string.IsNullOrEmpty(effectiveToken)) {
        Log("  SKIP — sin credenciales UltraMsg válidas");
        conn.Close();
        return;
    }
    if (string.IsNullOrEmpty(phone)) {
        Log("  SKIP — sin teléfono de muestra en la campaña");
        conn.Close();
        return;
    }

    Log($"  phone={phone}  campaignId={campaignId}");
    var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
    http.DefaultRequestHeaders.Add("X-N8N-Key", N8N_KEY);
    http.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId);

    var payload = $@"{{
        ""campaignId"":""{campaignId}"",
        ""agentId"":""{agentId}"",
        ""phone"":""{phone}"",
        ""clientName"":""{clientName.Replace("\"","")}"",
        ""policyNumber"":""TEST-001"",
        ""pendingAmount"":150.00,
        ""insurance"":""ASSA"",
        ""contactDataJson"":null,
        ""tenantConfig"":{{
            ""tenantId"":""{tenantId}"",
            ""ultraMsgInstanceId"":""{effectiveInstanceId}"",
            ""ultraMsgToken"":""{effectiveToken}""
        }}
    }}";

    var content = new StringContent(payload, Encoding.UTF8, "application/json");
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try {
        var resp = await http.PostAsync($"{API_BASE}/webhooks/campaign-send", content);
        sw.Stop();
        var body = await resp.Content.ReadAsStringAsync();
        Log($"  HTTP {(int)resp.StatusCode}  Tiempo: {sw.ElapsedMilliseconds}ms");
        Log($"  Respuesta completa: {body}");

        var parsed = JsonSerializer.Deserialize<JsonElement>(body);
        bool success = parsed.TryGetProperty("success", out var sv) && sv.GetBoolean();

        if (success) {
            Log("  ✓ SUCCESS — Claude generó mensaje y UltraMsg lo envió");
            var genMsg = parsed.TryGetProperty("generatedMessage", out var gm) ? gm.GetString() ?? "" : "";
            var extId  = parsed.TryGetProperty("externalMessageId", out var eid) ? eid.GetString() ?? "" : "";
            var convId = parsed.TryGetProperty("conversationId", out var cid) ? cid.GetString() ?? "" : "";
            Log($"  GeneratedMessage: {genMsg[..Math.Min(150,genMsg.Length)]}");
            Log($"  ExternalMsgId   : {extId}");

            // PASO 5b: contact-sent
            Log("\n=== PASO 5b: POST /api/campaigns/contact-sent ===");
            var csContent = new StringContent(
                $@"{{""campaignId"":""{campaignId}"",""phone"":""{phone}"",""externalMessageId"":""{extId}"",""conversationId"":""{convId}""}}",
                Encoding.UTF8, "application/json");
            var csResp = await http.PostAsync($"{API_BASE}/campaigns/contact-sent", csContent);
            var csBody = await csResp.Content.ReadAsStringAsync();
            Log($"  HTTP {(int)csResp.StatusCode}  Body: {csBody}");

            // Verificar BD
            Log("\n=== PASO 5c: BD después del test ===");
            var cmd5c = new SqlCommand($"SELECT ProcessedContacts, Status FROM Campaigns WHERE Id='{campaignId}'", conn);
            var r5c = cmd5c.ExecuteReader();
            if (r5c.Read()) {
                Log($"  ProcessedContacts: {r5c["ProcessedContacts"]}/{total}  Status: {r5c["Status"]}");
            }
            r5c.Close();

        } else {
            var errMsg = parsed.TryGetProperty("error", out var errEl) ? errEl.GetString() ?? "sin detalle" : "sin detalle";
            Log($"  ✗ FAILED (success=false): {errMsg}");
            if (errMsg.Contains("LlmApiKey") || errMsg.Contains("API key"))
                Log("  >>> CAUSA: LlmApiKey NULL en Tenant");
            else if (errMsg.Contains("UltraMsg") || errMsg.Contains("4"))
                Log("  >>> CAUSA: UltraMsg credenciales inválidas o instancia desconectada");
            else if (errMsg.Contains("prompt") || errMsg.Contains("Prompt"))
                Log("  >>> CAUSA: Sin prompt en el template");
            else if (errMsg.Contains("Claude") || errMsg.Contains("Anthropic"))
                Log("  >>> CAUSA: Claude API falló");
        }
    } catch (Exception ex) {
        Log($"  EXCEPTION: {ex.Message}");
    }

    conn.Close();
    Log($"\nLog: {LOG_FILE}");
    Log("======================================================");
}
