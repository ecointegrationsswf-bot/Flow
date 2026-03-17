# AgentFlow — Contexto completo del proyecto para Claude Code

## Qué es este proyecto

Plataforma multitenante de agentes IA para gestión de cobros, reclamos y renovaciones
de seguros vía WhatsApp, Email y SMS. Cada cliente (tenant/corredor) tiene su propio
número de WhatsApp, sus propios agentes IA configurados con prompt personalizado,
y sus propias campañas.

El sistema nació para reemplazar a **TalkIA** (plataforma anterior usada por Somos Seguros),
resolviendo sus problemas documentados en conversaciones reales de soporte (Feb-Mar 2026):

| Problema TalkIA | Solución en AgentFlow |
|----------------|----------------------|
| Desconexiones de WhatsApp relanzaban campañas antiguas | Redis SessionStore — estado persistente independiente del canal |
| Registros de gestión fantasma | GestionEvent con campo Origin verificable |
| Teléfonos inválidos (+507507) causaban bloqueos de Meta | FileProcessor valida antes de disparar mensajes |
| Dependencia de BSP externo para reconectar la línea | IChannelProvider propio — UltraMsg + Meta Cloud API |
| Dashboard con error "Error al obtener URL de Power BI" | Dashboard propio integrado en el portal |
| Saldos de morosidad incorrectos por aseguradora | Integración directa con SQL Server de Tobroker |

---

## Contexto de negocio

- **Clientes objetivo**: corredores de seguros en Panamá (ej: Somos Seguros, bajo YoSeguro)
- **Aseguradoras integradas**: Sura, ASSA, Ancón, IS, Mapfre, Génesis, Óptima, YoSeguro
- **Operativa de cobros**: el equipo descarga archivos de morosidad del sistema de cada
  aseguradora y los sube a la plataforma. El agente IA gestiona en horario de oficina
  (8am-5pm Panamá). Fuera de horario los mensajes se encolan.
- **Un solo número de WhatsApp** atiende cobros, reclamos y renovaciones. El dispatcher
  decide qué agente responde según el contexto de la conversación.
- **Roles del equipo**: ejecutivos de cobros (suben campañas, intervienen conversaciones),
  supervisores (ven todo el monitor), admins (configuran agentes y tenants).
- **Pólizas**: un cliente puede tener auto, vida e incendio simultáneamente.

---

## Decisiones de arquitectura tomadas — NO cambiar sin consultar

### Backend
- **Monolito modular** en ASP.NET Core 8 con 4 proyectos: Domain → Application → Infrastructure → API
- **Multitenancy**: cada query filtra por TenantId. Nunca datos cross-tenant.
- **MediatR** para commands y queries — un archivo por caso de uso.
- **Primary constructors** de C# 12 para inyección de dependencias.
- **Entity Framework Core 8** Code First — configuraciones en IEntityTypeConfiguration<T>.
- **Hangfire** para jobs programados (scheduler de campañas).

### Canales de WhatsApp — dos opciones coexisten
- **UltraMsg**: conexión por QR scan, número existente, sin proceso de Meta.
  Ideal para onboarding rápido de clientes. No es API oficial (riesgo de ban en alto volumen).
- **Meta Cloud API**: API oficial, número dedicado, para clientes enterprise o alto volumen.
- Ambos implementan **IChannelProvider** — el agente no sabe por cuál canal sale el mensaje.
  Configurado por tenant en Tenant.WhatsAppProvider (enum: UltraMsg | MetaCloudApi).

### Orquestación de flujos: n8n self-hosted
- n8n recibe webhooks de UltraMsg/Meta, normaliza el payload y hace POST a /api/webhooks/message.
- Permite modificar flujos (reintentos, horarios, escalaciones) sin tocar código .NET.
- Flujos definidos: mensaje entrante, campaña saliente, escalación a humano, reintento y cierre.

### Sesiones activas: Redis
- Clave: session:{tenantId}:{phone} — TTL 72 horas.
- Resuelve el problema crítico de TalkIA: al reconectar WhatsApp, el sistema consulta
  Redis primero. Si hay sesión activa, continúa sin relanzar campañas.

### Monitor en vivo: SignalR
- Hub: ConversationHub — ejecutivos se suscriben al grupo de su tenant.
- Eventos: MessageReceived, AgentReplied, ConversationEscalated, ConversationClosed.
- ConversationNotifier es el servicio inyectable que emite desde handlers.

### LLM: Claude (Anthropic)
- Modelo por defecto: **claude-sonnet-4-6**
- El agente declara intención con tag: [INTENT:cobros] | [INTENT:reclamos] | [INTENT:renovaciones] | [INTENT:humano] | [INTENT:cierre]
- AnthropicAgentRunner parsea el tag y lo limpia del texto visible al cliente.
- Temperature = 0.3, MaxTokens = 1024.

### Frontend: React
- React 18 + TypeScript + Vite + Tailwind CSS
- Proxy Vite → http://localhost:5000 (API) y WebSocket → /hubs
- Zustand para estado global, TanStack React Query para server state
- SignalR con @microsoft/signalr — hook useConversationHub

---

## Stack tecnológico completo

| Capa | Tecnología |
|------|-----------|
| Backend | ASP.NET Core 8 |
| Frontend | React 18 + TypeScript + Vite + Tailwind CSS |
| LLM | Claude API — claude-sonnet-4-6 |
| WhatsApp A | UltraMsg (onboarding rápido) |
| WhatsApp B | Meta Cloud API (enterprise) |
| Flujos | n8n self-hosted |
| Base de datos | SQL Server 2022 Enterprise |
| ORM | Entity Framework Core 8 |
| Sesiones | Redis — StackExchange.Redis |
| Tiempo real | SignalR |
| Jobs | Hangfire |
| Mediador | MediatR 12.2 |
| Validación | FluentValidation 11.9 |
| Auth | JWT Bearer |
| Deploy | Servidor propio / on-premise |

---

## Estructura de la solución

```
AgentFlow.sln
├── src/
│   ├── AgentFlow.Domain/
│   │   ├── Entities/
│   │   │   ├── Tenant.cs
│   │   │   ├── AgentDefinition.cs
│   │   │   ├── Campaign.cs
│   │   │   ├── CampaignContact.cs
│   │   │   ├── Conversation.cs
│   │   │   ├── Message.cs
│   │   │   ├── GestionEvent.cs
│   │   │   └── AppUser.cs
│   │   ├── Enums/
│   │   │   ├── AgentType.cs           # Cobros | Reclamos | Renovaciones | General
│   │   │   ├── ChannelType.cs         # WhatsApp | Email | Sms
│   │   │   ├── ConversationStatus.cs  # Active | WaitingClient | EscalatedToHuman | Closed | Unresponsive
│   │   │   ├── CampaignTrigger.cs     # FileUpload | PolicyEvent | DelinquencyEvent | Manual
│   │   │   ├── ProviderType.cs        # UltraMsg | MetaCloudApi
│   │   │   └── GestionResult.cs       # Pending | PaymentCommitted | PaymentReceived | Rejected | ...
│   │   └── Interfaces/
│   │       ├── IChannelProvider.cs
│   │       ├── IAgentRunner.cs
│   │       ├── IContextDispatcher.cs
│   │       ├── IConversationRepository.cs
│   │       ├── ISessionStore.cs
│   │       └── ITenantContext.cs
│   ├── AgentFlow.Application/
│   │   └── Modules/
│   │       ├── Webhooks/ProcessIncomingMessageCommand.cs
│   │       ├── Campaigns/StartCampaignCommand.cs
│   │       ├── Monitor/GetActiveConversationsQuery.cs
│   │       └── Agents/  (pendiente CRUD)
│   ├── AgentFlow.Infrastructure/
│   │   ├── Persistence/AgentFlowDbContext.cs + Configurations/
│   │   ├── Channels/UltraMsg/UltraMsgProvider.cs
│   │   ├── Channels/MetaCloudApi/  (pendiente)
│   │   ├── AI/AnthropicAgentRunner.cs
│   │   └── Session/RedisSessionStore.cs
│   └── AgentFlow.API/
│       ├── Controllers/ (Webhook, Monitor, Campaigns, Agents)
│       ├── Hubs/ConversationHub.cs + ConversationNotifier
│       ├── Middleware/TenantMiddleware.cs
│       └── Program.cs
└── frontend/
    └── src/
        ├── modules/monitor/components/MonitorPage.tsx
        ├── modules/campaigns/  (pendiente)
        ├── modules/agents/     (pendiente)
        └── shared/
            ├── api/client.ts
            └── hooks/useConversationHub.ts
```

---

## Flujo principal — mensaje entrante

```
Cliente WhatsApp → UltraMsg/Meta webhook → n8n (normaliza) → POST /api/webhooks/message
    → TenantMiddleware (resuelve TenantId desde JWT o header X-Tenant-Id)
    → ProcessIncomingMessageCommand (MediatR)
    → IContextDispatcher.DispatchAsync()
        [1] Redis: ¿sesión activa? → SÍ: continúa con agente activo
        [2] BD: ¿contacto en campaña activa? → SÍ: usa agente de la campaña
        [3] LLM: clasifica intención → cobros | reclamos | renovaciones | humano
    → IAgentRunner.RunAsync() → Claude API con system prompt + historial + contexto cliente
    → IChannelProvider.SendMessageAsync() → UltraMsg o Meta según tenant
    → GestionEvent (Origin="agent:{agentType}") + SessionState Redis + Message BD
    → ConversationNotifier → SignalR → MonitorPage React
```

---

## Flujo de campañas — dos disparadores

### Por archivo (ejecutivo)
```
Sube CSV/Excel → FileProcessor valida teléfonos → Campaign + CampaignContacts en BD
→ Hangfire/n8n envía mensaje inicial en horario configurado
```

### Por evento automático
```
Tobroker detecta mora/vencimiento → n8n/Hangfire → Campaign con Trigger=PolicyEvent
→ mismo flujo de envío
```

---

## Entidades — campos clave

### Tenant
```
WhatsAppProvider (UltraMsg|MetaCloudApi), WhatsAppPhoneNumber, WhatsAppApiToken (cifrado),
WhatsAppInstanceId (solo UltraMsg), BusinessHoursStart/End, TimeZone="America/Panama"
```

### AgentDefinition
```
AgentType, SystemPrompt, Tone, Language="es", AvatarName
EnabledChannels (List<ChannelType>), SendFrom/SendUntil
MaxRetries=3, RetryIntervalHours=24, InactivityCloseHours=72
CloseConditionKeyword (ej: "pagó"), LlmModel="claude-sonnet-4-6"
Temperature=0.3, MaxTokens=1024
```

### Conversation
```
ClientPhone (E.164), Channel, ActiveAgentId, CampaignId
Status (Active|WaitingClient|EscalatedToHuman|Closed|Unresponsive)
IsHumanHandled, HandledByUserId, GestionResult, LastActivityAt
```

### GestionEvent
```
Origin = "agent:cobros" | "agent:reclamos" | "human:{userId}"
Result (GestionResult), Notes, OccurredAt
```

### SessionState (Redis — no es entidad EF)
```
Clave: "session:{tenantId}:{phone}" — TTL 72h
{ ConversationId, AgentId, AgentType, CampaignId, IsHumanHandled, LastActivityAt }
```

---

## Interfaces clave

### IChannelProvider
```csharp
Task<SendResult> SendMessageAsync(SendMessageRequest request, CancellationToken ct)
Task<MessageStatusResult> GetMessageStatusAsync(string externalMessageId, CancellationToken ct)
bool ValidateWebhookSignature(string payload, string signature)
```

### IAgentRunner
```csharp
Task<AgentResponse> RunAsync(AgentRunRequest request, CancellationToken ct)
// AgentResponse: ReplyText, DetectedIntent, ConfidenceScore, ShouldEscalate, ShouldClose, TokensUsed
// AgentRunRequest: Agent, Conversation, IncomingMessage, RecentHistory, ClientContext (dict)
```

### IContextDispatcher
```csharp
Task<DispatchResult> DispatchAsync(DispatchRequest request, CancellationToken ct)
// DispatchResult: ExistingConversationId, SelectedAgentId, Intent, IsExistingSession, IsCampaignContact
```

### ISessionStore
```csharp
Task<SessionState?> GetAsync(Guid tenantId, string phone, CancellationToken ct)
Task SetAsync(Guid tenantId, string phone, SessionState state, TimeSpan? expiry, CancellationToken ct)
Task RemoveAsync(Guid tenantId, string phone, CancellationToken ct)
Task<bool> ExistsAsync(Guid tenantId, string phone, CancellationToken ct)
```

---

## Monitor en vivo — especificación

- Lista izquierda: nombre cliente, preview último mensaje, badge agente, tiempo última actividad.
  Badge rojo si lleva más de 8 minutos sin respuesta del cliente.
- Panel derecho: historial completo con burbujas diferenciadas (agente IA / cliente / ejecutivo).
- Acciones: "Tomar conversación" (pausa IA), "Pausar IA", enviar mensaje directo, "Reactivar IA".
- Filtros: por tipo agente, por estado, por canal.
- SignalR — no polling. Alertas visuales cuando intent=humano o confianza baja.

---

## Validación de teléfonos panameños

- Celulares: +507 6xxx-xxxx (8 dígitos después del código)
- Fijos: +507 2xx-xxxx o 3xx-xxxx (7 dígitos)
- Normalizar: quitar espacios, guiones, paréntesis. Agregar +507 si solo tiene 7-8 dígitos.
- Inválidos: menos de 7 dígitos, solo ceros, formato +507507 (bug documentado de TalkIA).
- Detectar duplicados dentro del archivo y contra campañas activas del tenant.

---

## Tareas pendientes — orden de implementación

### Prioridad 1 — End-to-end funcional
1. **ContextDispatcher** — `Infrastructure/Dispatching/ContextDispatcher.cs`
   Implementa IContextDispatcher. Lógica: Redis → BD campaña → LLM clasificación.

2. **ConversationRepository** — `Infrastructure/Persistence/Repositories/ConversationRepository.cs`
   Implementa IConversationRepository. Queries siempre filtradas por TenantId.

3. **Migración EF Core inicial**
   ```bash
   dotnet ef migrations add InitialCreate \
     --project src/AgentFlow.Infrastructure \
     --startup-project src/AgentFlow.API
   dotnet ef database update \
     --project src/AgentFlow.Infrastructure \
     --startup-project src/AgentFlow.API
   ```

### Prioridad 2 — Campañas
4. **FileProcessor** — `Application/Modules/Campaigns/FileProcessor.cs`
   CSV y Excel. Validación E.164 Panamá. Detección de duplicados.

5. **StartCampaignCommand handler** — crea Campaign + CampaignContacts, programa en Hangfire.

6. **MetaCloudApiProvider** — valida HMAC-SHA256, endpoint Graph API v18.

### Prioridad 3 — Monitor completo
7. **TakeConversationCommand** — pausa IA, asigna ejecutivo, SignalR "ConversationTaken".
8. **HumanReplyCommand** — envía mensaje ejecutivo, registra GestionEvent, SignalR.
9. **ReactivateAgentCommand** — devuelve control al agente IA.

### Prioridad 4 — Frontend
10. **AgentBuilder** — formulario completo para crear/editar AgentDefinition.
11. **CampaignUpload** — drag & drop CSV/Excel con preview de validación en tiempo real.
12. **Dashboard** — KPIs: tasa de gestión exitosa, distribución por agente, campañas activas.

### Prioridad 5 — Robustez
13. Cifrado de tokens en BD (Data Protection API de ASP.NET Core).
14. Rate limiting en WebhookController.
15. Health checks para SQL Server, Redis y Anthropic API.
16. Logging estructurado con Serilog.

---

## Convenciones de código

- Nombres de clases y métodos en inglés, comentarios de negocio en español
- Commands → sufijo Command, Queries → Query, Handlers → Handler
- Nunca lógica de negocio en Controllers
- async/await en toda la cadena — nunca .Result ni .Wait()
- CancellationToken ct en todos los métodos async
- Primary constructors C# 12 para inyección
- EF configuraciones en IEntityTypeConfiguration<T> separadas
- Nunca hardcodear API keys — usar variables de entorno / appsettings.Development.json

---

## Configuración local

Crear `src/AgentFlow.API/appsettings.Development.json` (NO commitear):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=AgentFlowDb;Trusted_Connection=True;TrustServerCertificate=True;",
    "Redis": "localhost:6379"
  },
  "Anthropic": { "ApiKey": "sk-ant-api03-..." },
  "Meta": {
    "VerifyToken": "token-que-configuras-en-meta",
    "AppSecret": "app-secret-de-meta"
  },
  "Auth": {
    "Authority": "https://tu-servidor-auth",
    "Audience": "agentflow-api"
  }
}
```

Para UltraMsg: InstanceId y Token se guardan cifrados en BD por tenant.

---

## Comandos útiles

```bash
# Compilar
dotnet build AgentFlow.sln

# Correr API
dotnet run --project src/AgentFlow.API

# Frontend
cd frontend && npm install && npm run dev

# Migración nueva
dotnet ef migrations add NombreMigracion \
  --project src/AgentFlow.Infrastructure \
  --startup-project src/AgentFlow.API

# Aplicar migraciones
dotnet ef database update \
  --project src/AgentFlow.Infrastructure \
  --startup-project src/AgentFlow.API
```

---

## Integración con Tobroker

Tobroker corre en SQL Server 2022 Enterprise. El agente de cobros puede consultar
saldos, historial de pagos y fechas de vencimiento. Preferir vista o stored procedure
dedicado para no acoplar el sistema a la estructura interna de Tobroker.
Consultar al DBA antes de acceder directamente a las tablas.

---

## Decisiones pendientes

- **Autenticación**: ¿Keycloak/Identity Server o JWT generado internamente?
- **Cifrado tokens BD**: ¿Data Protection API o AES manual?
- **n8n**: ¿mismo servidor que el API o servidor dedicado?
- **Email**: ¿SendGrid o Azure Communication Services?
- **SMS**: ¿Twilio SMS o Azure Communication Services?
- **Backups on-premise**: estrategia para SQL Server y Redis.
