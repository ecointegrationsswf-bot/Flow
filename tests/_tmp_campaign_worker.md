AgentFlow
Campaign Automation Worker
Plan de Implementación por Fases — Worker Service + Automatizaciones de Campaña

Documento
Plan de Implementación Técnico
Versión
2.0  —  Abril 2026
Proyecto
AgentFlow — Sistema de Atención por WhatsApp con IA
Módulo
ScheduledWebhookWorker + Campaign Automation
Prerequisito
Webhook Contract System v2.0 (ActionExecutorService implementado)
Estado
✅ Listo para implementar
Total estimado
9 días de desarrollo — 3 migraciones de BD (32, 33a, 33b, 33c)


# 1. Visión General

Este documento unifica el diseño del ScheduledWebhookWorker (infraestructura de jobs) con las cuatro automatizaciones del ciclo de vida de campaña (seguimientos, cierre automático, etiquetado IA y webhook de resultado). Están organizados en tres fases de implementación secuenciales que construyen una sobre la otra.

Principio de diseño: el Worker es la infraestructura. Las automatizaciones son los jobs que corren sobre ella. Las fases están ordenadas para que cada una sea completamente funcional antes de pasar a la siguiente — no hay dependencias cruzadas entre fases.


## 1.1 Qué construye cada fase

Fase
Nombre
Duración
Entregable funcional
1
Worker Service + EventDispatcher
4 días
La infraestructura de jobs está operativa. Jobs Cron, EventBased y DelayFromEvent se ejecutan correctamente. El admin puede crear y monitorear jobs desde la UI.
2
Seguimientos + Cierre Automático
2 días
Las campañas envían mensajes de seguimiento en los intervalos configurados y se cierran automáticamente al vencerse el plazo definido.
3
Etiquetado IA + Webhook de Resultado
3 días
Las conversaciones se clasifican automáticamente con IA diariamente. El cliente recibe el resultado de cada conversación en su endpoint configurado.


## 1.2 Arquitectura completa — componentes y relaciones

                    ┌─────────────────────────────────────┐
                    │     ScheduledWebhookWorker          │  ← FASE 1
                    │  (BackgroundService — 60s polling)  │
                    └────────────┬────────────────────────┘
                                 │ detecta jobs pendientes
                    ┌────────────▼────────────────────────┐
                    │     ScheduledWebhookJobs (BD)       │
                    │  TriggerType: Cron|Event|Delay      │
                    │  Scope: AllTenants|PerCampaign|     │
                    │         PerConversation             │
                    └──┬─────────────┬───────────────┬────┘
                       │             │               │
              ┌────────▼──┐  ┌───────▼───┐  ┌───────▼──────────┐
              │FollowUp   │  │AutoClose  │  │LabelingJob       │  ← FASES 2 y 3
              │Executor   │  │Executor   │  │+ WebhookResult   │
              └────────┬──┘  └───────┬───┘  └───────┬──────────┘
                       │             │               │
              ┌────────▼─────────────▼───────────────▼──────────┐
              │          ActionExecutorService                   │  ← ya implementado
              │  PayloadBuilder · HttpDispatcher · OutputInterp  │
              └──────────────────────────────────────────────────┘
 
  WebhookEventDispatcher ── publica eventos ──► crea jobs en BD
  (CampaignStarted, CampaignContactSent, ConversationClosed, ...)



# 2. Modelo de Datos — Migraciones 32 y 33

Todas las migraciones de las tres fases se definen aquí para tener visibilidad completa antes de comenzar. Cada fase solo ejecuta sus propias migraciones.

Sin breaking changes en ninguna migración. Todos los campos nuevos tienen DEFAULT o son nullable. Las campañas y conversaciones activas no se ven afectadas.


## 2.1 Migración 32 — infraestructura del Worker (Fase 1)

-- MIGRACIÓN 32a: tabla principal de jobs
CREATE TABLE ScheduledWebhookJobs
(
    Id                  uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
    ActionDefinitionId  uniqueidentifier NOT NULL REFERENCES ActionDefinitions(Id),
    TriggerType         nvarchar(20)     NOT NULL,
    CONSTRAINT CK_SWJ_TriggerType CHECK (TriggerType IN ('Cron','EventBased','DelayFromEvent')),
    CronExpression      nvarchar(100)    NULL,
    TriggerEvent        nvarchar(100)    NULL,
    -- Valores: CampaignStarted | CampaignFinished | CampaignContactSent
    --          ConversationClosed | ConversationEscalated
    DelayMinutes        int              NULL,
    Scope               nvarchar(20)     NOT NULL DEFAULT 'AllTenants',
    CONSTRAINT CK_SWJ_Scope CHECK (Scope IN ('AllTenants','PerCampaign','PerConversation')),
    IsActive            bit              NOT NULL DEFAULT 1,
    NextRunAt           datetime2        NULL,
    LastRunAt           datetime2        NULL,
    LastRunStatus       nvarchar(20)     NULL,
    LastRunSummary      nvarchar(1000)   NULL,
    ConsecutiveFailures int              NOT NULL DEFAULT 0,
    CreatedAt           datetime2        NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt           datetime2        NOT NULL DEFAULT GETUTCDATE()
);
 
-- MIGRACIÓN 32b: historial de ejecuciones
CREATE TABLE ScheduledWebhookJobExecutions
(
    Id              uniqueidentifier PRIMARY KEY DEFAULT NEWID(),
    JobId           uniqueidentifier NOT NULL REFERENCES ScheduledWebhookJobs(Id),
    StartedAt       datetime2        NOT NULL,
    CompletedAt     datetime2        NOT NULL,
    Status          nvarchar(20)     NOT NULL,
    TotalRecords    int              NOT NULL DEFAULT 0,
    SuccessCount    int              NOT NULL DEFAULT 0,
    FailureCount    int              NOT NULL DEFAULT 0,
    ErrorDetail     nvarchar(max)    NULL,
    TriggeredBy     nvarchar(50)     NULL,
    ContextId       nvarchar(200)    NULL
);
 
-- MIGRACIÓN 32c: ScheduleConfig en ActionDefinitions
ALTER TABLE ActionDefinitions ADD ScheduleConfig nvarchar(max) NULL;


## 2.2 Migración 33 — automatizaciones de campaña (Fases 2 y 3)

-- MIGRACIÓN 33a: configuración de automatizaciones en CampaignTemplates
ALTER TABLE CampaignTemplates ADD
    FollowUpScheduleJson   nvarchar(500)  NULL,   -- '[24, 48, 72]' horas
    FollowUpMessagesJson   nvarchar(max)  NULL,   -- ['Mensaje 1','Mensaje 2','Mensaje 3']
    AutoCloseHours         int            NULL,   -- horas desde inicio hasta cierre auto
    AutoCloseMessage       nvarchar(1000) NULL,   -- mensaje enviado al cerrar
    LabelingJobHourUtc     int            NULL,   -- hora UTC del job diario (0-23)
    ResultWebhookUrl       nvarchar(500)  NULL,   -- endpoint del cliente
    ResultOutputSchema     nvarchar(max)  NULL;   -- JSON OutputSchema del resultado
 
-- MIGRACIÓN 33b: estado de etiquetado en Conversations
ALTER TABLE Conversations ADD
    LabelId                uniqueidentifier NULL REFERENCES CampaignLabels(Id),
    LabeledAt              datetime2        NULL,
    ResultWebhookSentAt    datetime2        NULL,
    ResultWebhookStatus    int              NULL;
 
-- MIGRACIÓN 33c: control de seguimientos en CampaignContacts
ALTER TABLE CampaignContacts ADD
    FollowUpsSentJson      nvarchar(200)  NULL DEFAULT '[]';


FASE 1
4 días
Worker Service + EventDispatcher
Infraestructura base de jobs: polling, tres tipos de trigger, paralelismo controlado, circuit breaker y UI de administración.


## Objetivo de la fase

Al finalizar esta fase el sistema tiene un BackgroundService operativo que monitorea la BD cada 60 segundos, detecta jobs pendientes y los ejecuta llamando al ActionExecutorService existente. El admin puede crear y monitorear jobs desde la UI. Las fases 2 y 3 solo agregan nuevos tipos de ejecutores sobre esta base.


## Migración de BD

Ejecutar Migración 32 completa (32a + 32b + 32c). Ver sección 2.1.


## Tareas día a día

Día
Duración
Tarea
Verificación
1
~4h
Migración 32 (32a + 32b + 32c).
Entidades C#: ScheduledWebhookJob, ScheduledWebhookJobExecution.
IScheduledJobRepository + ScheduledJobRepository: GetPendingJobsAsync, SetStatusAsync, UpdateAfterRunAsync, PauseJobAsync.
IJobExecutionRepository + JobExecutionRepository: InsertAsync, InsertPendingAsync.
Migraciones aplicadas sin error. Insertar un job de prueba manualmente en BD. El repositorio lo devuelve correctamente. ✅
2
~5h
ScheduledWebhookWorker (BackgroundService): ciclo de 60s, marcado Running, GetPendingJobsAsync.
DispatchJobAsync con routing por Scope: AllTenants (SemaphoreSlim 10), PerCampaign, PerConversation.
CompleteJobAsync: cálculo de NextRunAt con Cronos para jobs Cron.
Reset automático de jobs atascados en Running por más de 10 minutos.
Registro en Program.cs.
Worker arranca con la app. Un job Cron de prueba ('* * * * *') se ejecuta cada minuto. Log muestra el ciclo correctamente. ✅
3
~5h
IWebhookEventDispatcher + WebhookEventDispatcher: DispatchAsync con cálculo de NextRunAt según TriggerType.
Circuit breaker por (tenantId:actionSlug) con IMemoryCache: umbral 5 fallos, ventana 5 min.
Inyección de IWebhookEventDispatcher en CampaignDispatcherJob (CampaignStarted, CampaignFinished).
Inyección en ProcessIncomingMessageCommand (ConversationClosed).
Inyección en BrainService (ConversationEscalated).
Finalizar una campaña de prueba dispara un job EventBased visible en BD. El Worker lo ejecuta en el siguiente ciclo. ✅
4
~5h
UI Admin — pantalla Scheduled Jobs:
  · Listado con estado, próxima ejecución y último resultado.
  · Formulario de creación: campos comunes + dinámicos por TriggerType.
  · Para Cron: validación de expresión + preview próximas 5 ejecuciones.
  · Para EventBased/DelayFromEvent: selector de evento + delay.
  · Botón 'Ejecutar ahora' (manual run).
  · Panel de historial con detalle de fallos.
Endpoint POST /api/scheduled-jobs/{id}/run-now.
Admin crea un job Cron desde UI. Lo ejecuta manualmente. El historial muestra 'Success 1/1'. ✅


## Archivos de esta fase


Archivo
Descripción
NUEVO
ScheduledWebhookWorker.cs
BackgroundService. Ciclo de polling, dispatcher por scope, circuit breaker.
NUEVO
WebhookEventDispatcher.cs
Crea executions pendientes al ocurrir eventos del sistema.
NUEVO
IScheduledJobRepository.cs
Contrato de acceso a ScheduledWebhookJobs.
NUEVO
ScheduledJobRepository.cs
Implementación del repositorio.
NUEVO
IJobExecutionRepository.cs
Contrato de acceso a ScheduledWebhookJobExecutions.
NUEVO
JobExecutionRepository.cs
Implementación del repositorio.
NUEVO
JobRunResult.cs
DTO de resultado del run.
NUEVO
ScheduledJobsController.cs
CRUD + endpoint /run-now.
MOD
CampaignDispatcherJob.cs
+2 llamadas a IWebhookEventDispatcher (CampaignStarted, CampaignFinished).
MOD
ProcessIncomingMessageCommand.cs
+1 llamada a IWebhookEventDispatcher (ConversationClosed).
MOD
BrainService.cs
+1 llamada a IWebhookEventDispatcher (ConversationEscalated).
MOD
Program.cs
+4 líneas de registro DI.


FASE 2
2 días
Seguimientos Automáticos + Cierre Automático
Las campañas envían seguimientos en los intervalos configurados y se cierran al vencerse el plazo. Construye sobre la infraestructura de la Fase 1.


## Objetivo de la fase

Al finalizar esta fase, una campaña con FollowUpScheduleJson='[24,48,72]' y AutoCloseHours=72 envía automáticamente tres mensajes de seguimiento a los clientes que no respondieron, y cierra todas las conversaciones activas al cumplirse 72 horas.


## Migración de BD

Ejecutar Migración 33a (FollowUpScheduleJson, FollowUpMessagesJson, AutoCloseHours, AutoCloseMessage) y Migración 33c (FollowUpsSentJson en CampaignContacts). Ver sección 2.2.


## Mecánica — cómo se conecta con la Fase 1

// Al iniciar la campaña (CampaignDispatcherJob — ya modificado en Fase 1):
 
// 1. Por cada contacto: crear jobs de seguimiento DelayFromEvent
var schedule = JsonSerializer.Deserialize<List<int>>(campaign.FollowUpScheduleJson ?? "[]");
for (int i = 0; i < schedule.Count; i++)
    await _eventDispatcher.DispatchAsync(
        eventName:    "CampaignContactSent",
        contextId:    $"{campaignContact.Id}:{i}",
        tenantId:     campaign.TenantId,
        delayMinutes: schedule[i] * 60);
 
// 2. Para la campaña: crear UN job de cierre DelayFromEvent
if (campaign.AutoCloseHours.HasValue)
    await _eventDispatcher.DispatchAsync(
        eventName:    "CampaignStarted",
        contextId:    campaign.Id.ToString(),
        tenantId:     campaign.TenantId,
        delayMinutes: campaign.AutoCloseHours.Value * 60);
 
// El Worker de Fase 1 detecta y ejecuta estos jobs automáticamente.
// Solo necesitamos registrar los dos nuevos executors.


## Tareas día a día

Día
Duración
Tarea
Verificación
5
~5h
Migración 33a (FollowUpScheduleJson, AutoCloseHours y campos relacionados).
Migración 33c (FollowUpsSentJson en CampaignContacts).
FollowUpExecutor: lógica de omisión (conv no activa, índice ya enviado), resolución de variables {nombre} etc., envío WhatsApp.
Integración en CampaignDispatcherJob: crear jobs DelayFromEvent por cada intervalo al enviar el primer mensaje.
Conexión de campos UI existentes (imagen 1: horas de seguimiento, cierre automático) con los nuevos campos de BD.
Guardado de FollowUpMessagesJson: un mensaje por intervalo.
Campaña de prueba con seguimientos a 1h y 2h. Los mensajes llegan con el texto correcto. Los contactos que responden NO reciben el segundo seguimiento. ✅
6
~4h
CampaignAutoCloseExecutor: cierre en lote, envío de AutoCloseMessage, disparo de ConversationClosed y CampaignFinished por cada conversación cerrada.
Integración en CampaignDispatcherJob: crear job de cierre DelayFromEvent al iniciar la campaña.
Test de regresión: verificar que los seguimientos NO se envían a conversaciones cerradas por el AutoClose.
Campaña configurada con AutoCloseHours=1h en entorno de prueba. Al cumplirse 1h todas las conversaciones activas se cierran. Los eventos ConversationClosed se disparan. ✅


## Reglas de negocio — cuándo NO enviar seguimiento

Condición
Comportamiento
Cliente respondió a la campaña
Conversación no está en estado Active → seguimiento omitido silenciosamente.
Conversación cerrada manualmente
Status != Active → omitido. Log INFO.
Conversación escalada a humano
Status = Escalated → omitido. El ejecutivo maneja al cliente.
Seguimiento ya enviado (retry)
FollowUpsSentJson contiene el índice → omitido. Idempotencia garantizada.
Campaña cancelada
Campaign.Status = Cancelled → todos los jobs pendientes son ignorados.


## Archivos de esta fase


Archivo
Descripción
NUEVO
FollowUpExecutor.cs
Envío de mensajes de seguimiento con omisión inteligente e idempotencia.
NUEVO
CampaignAutoCloseExecutor.cs
Cierre en lote de conversaciones activas al vencerse AutoCloseHours.
MOD
CampaignDispatcherJob.cs
+creación de jobs de seguimiento y cierre al enviar la campaña.
MOD
ScheduledWebhookWorker.cs
+routing a FollowUpExecutor y AutoCloseExecutor según actionSlug.


FASE 3
3 días
Etiquetado IA + Webhook de Resultado
Claude clasifica automáticamente cada conversación cerrada. El cliente recibe el resultado en su endpoint configurado usando el OutputSchema del Webhook Contract System.


## Objetivo de la fase

Al finalizar esta fase, todas las noches a la hora configurada, Claude lee el historial de cada conversación cerrada sin etiquetar y asigna la etiqueta más apropiada del maestro de campaña. Una vez etiquetada, el sistema construye el JSON de resultado definido por el cliente y lo envía a su endpoint.


## Migración de BD

Ejecutar Migración 33b (LabelId, LabeledAt, ResultWebhookSentAt, ResultWebhookStatus en Conversations). Ver sección 2.2.


## Cómo Claude clasifica — el prompt de etiquetado

El clasificador llama a Claude directamente (no como agente de conversación). Recibe el historial completo + la lista de etiquetas con sus palabras clave. Responde únicamente con JSON estructurado.

// System prompt del clasificador — fijo
"""
Eres un clasificador de conversaciones de atención al cliente.
Tu única tarea es leer el historial completo de una conversación
y asignarle la etiqueta que mejor describe su resultado.
 
REGLAS:
1. Analiza el historial COMPLETO de la conversación.
2. Elige EXACTAMENTE UNA etiqueta de la lista proporcionada.
3. Usa las palabras clave de cada etiqueta como guía semántica, no como match literal.
4. Responde ÚNICAMENTE con JSON válido, sin texto adicional.
5. Formato exacto: {"labelSlug":"...","confidence":0.0,"reasoning":"..."}
"""
 
// Prompt de usuario — construido por conversación
## ETIQUETAS DISPONIBLES
- Slug: compromiso_pago
  Nombre: Compromiso de pago
  Palabras clave: Voy a pagar, Pago la próxima semana
 
- Slug: cliente_errado
  Nombre: Cliente errado
  Palabras clave: No soy yo, ya no tengo esa propiedad
 
## HISTORIAL DE LA CONVERSACIÓN
[09:01] Agente: Hola Juan, le contactamos por su póliza #98882 con saldo de $450.
[09:15] Cliente: Sí, voy a pagar la próxima semana el viernes.
[09:16] Agente: Perfecto, le confirmamos el compromiso de pago para el viernes.
 
// Respuesta de Claude:
{ "labelSlug": "compromiso_pago", "confidence": 0.97,
  "reasoning": "El cliente confirmó pago el próximo viernes." }


## Webhook de resultado — reutiliza el Webhook Contract System

El JSON enviado al cliente se construye con el PayloadBuilder y el OutputSchema ya implementados. Solo se agregan nuevos sourceKeys específicos del resultado de la conversación.

// ResultOutputSchema configurado por el admin en el Webhook Builder (Paso 0-4)
// Ejemplo: campaña de cobros
{
  "fields": [
    { "fieldPath":"nroPoliza",      "sourceKey":"contact.idNumber",
      "sourceType":"system",         "dataType":"string" },
    { "fieldPath":"estatus",         "sourceKey":"conversation.label.name",
      "sourceType":"system",         "dataType":"string" },
    { "fieldPath":"fechaCompromiso", "sourceKey":"conversation.label.extractedDate",
      "sourceType":"system",         "dataType":"date" },
    { "fieldPath":"keyvalue",        "sourceKey":"contact.externalId",
      "sourceType":"system",         "dataType":"string" }
  ]
}
 
// JSON enviado al endpoint del cliente:
{
  "nroPoliza":       "98882",
  "estatus":         "Compromiso de pago",
  "fechaCompromiso": "2026-04-17",
  "keyvalue":        "988882"
}


## Nuevos sourceKeys disponibles en el ResultOutputSchema

sourceKey
Tipo
Descripción
conversation.label.name
string
Nombre de la etiqueta asignada por el clasificador IA.
conversation.label.slug
string
Slug de la etiqueta.
conversation.label.confidence
number
Nivel de confianza del clasificador (0.0 a 1.0).
conversation.label.reasoning
string
Justificación del clasificador IA.
conversation.closedAt
date
Fecha y hora de cierre de la conversación.
conversation.closeReason
string
AutoClose | ManualClose | AgentClose | EscalatedClose
conversation.messageCount
number
Total de mensajes intercambiados.
contact.externalId
string
ID externo del contacto en el sistema del cliente (nroPoliza, cédula, etc.).
campaign.externalRef
string
Referencia externa de la campaña configurada por el tenant.


## Tareas día a día

Día
Duración
Tarea
Verificación
7
~4h
Migración 33b (LabelId, LabeledAt, ResultWebhookSentAt en Conversations).
ConversationLabelingJob: GetClosedUnlabeledAsync, LabelConversationAsync con SemaphoreSlim(10).
BuildLabelingPrompt: historial + etiquetas.
Llamada a Claude como clasificador: maxTokens=200, parsing de JSON de respuesta.
Persistencia de LabelId y LabeledAt en Conversation.
Job clasifica 10 conversaciones reales de prueba. Las etiquetas asignadas son verificadas manualmente como correctas. ✅
8
~5h
SendResultWebhookAsync integrado en el job de etiquetado.
SystemContextBuilder.BuildResultContextAsync con los nuevos sourceKeys de resultado.
ResultOutputSchema en Webhook Builder — Paso 0 de configuración del tenant.
Idempotencia: verificar ResultWebhookSentAt antes de enviar.
Registro de ResultWebhookStatus en Conversation.
Webhook de resultado enviado con payload correcto al endpoint de prueba del cliente tras el etiquetado. ✅
9
~5h
UI: campo hora de etiquetado (LabelingJobHourUtc) en tab Etiquetas del maestro.
UI: sección 'Webhook de resultado' en tab Etiquetas: campo URL + botón para abrir Webhook Builder.
UI Dashboard campaña: gráfico de dona con conteo por etiqueta.
UI Dashboard campaña: tabla de conversaciones etiquetadas con filtro por etiqueta.
Indicador de estado del webhook (✅ Enviado / ⏳ Pendiente / ❌ Fallido) con botón de reenvío manual para fallidos.
Admin configura etiquetado y webhook desde UI. Dashboard muestra distribución de etiquetas. El reenvío manual funciona. ✅


## Archivos de esta fase


Archivo
Descripción
NUEVO
ConversationLabelingJob.cs
Job diario: carga conversaciones, llama a Claude, persiste etiqueta, dispara webhook.
NUEVO
LabelingResult.cs
DTO: labelSlug, confidence, reasoning.
MOD
SystemContextBuilder.cs
+BuildResultContextAsync con nuevos sourceKeys de resultado de conversación.
MOD
ScheduledWebhookWorker.cs
+routing a ConversationLabelingJob según actionSlug.



# 4. Flujo Completo del Ciclo de Vida de una Campaña

Ejemplo: campaña de cobros — 500 contactos, seguimientos a 24h y 48h, cierre automático a 72h, etiquetado diario a las 11pm UTC.

Tiempo
Fase
Job
Qué ocurre
Hora 0
1+2
CampaignDispatcherJob
500 mensajes de WhatsApp enviados. Se crean 1000 jobs DelayFromEvent de seguimiento (500×2) y 1 job de cierre a las 72h. Todos visibles en ScheduledWebhookJobs.
Hora 24
2
FollowUpExecutor
Para cada contacto sin respuesta: envía el primer mensaje de seguimiento. Los que respondieron son omitidos silenciosamente.
23:00 día 1
3
ConversationLabelingJob
Clasifica las conversaciones cerradas hasta ese momento. Envía webhook de resultado al endpoint del cliente por cada una etiquetada.
Hora 48
2
FollowUpExecutor
Segundo seguimiento para los contactos que aún no respondieron.
Hora 72
2
AutoCloseExecutor
Cierra todas las conversaciones activas. Envía AutoCloseMessage. Dispara CampaignFinished y ConversationClosed por cada una.
23:00 día 3
3
ConversationLabelingJob
Clasifica las conversaciones cerradas por el AutoClose. Envía webhooks de resultado. El Dashboard muestra la distribución final de etiquetas.



# 5. Resumen Ejecutivo de Cambios por Fase

Fase
Archivos nuevos
Archivos mod.
Migraciones
Días
Fase 1
9 archivos nuevos
4 archivos modificados
Migración 32 (32a+32b+32c)
4 días
Fase 2
2 archivos nuevos
2 archivos modificados
Migración 33a + 33c
2 días
Fase 3
2 archivos nuevos
2 archivos modificados
Migración 33b
3 días
TOTAL
13 archivos nuevos
8 archivos modificados
5 scripts de migración
9 días

Sin breaking changes en ninguna fase. Rollback inmediato en cualquier punto: desregistrar el HostedService en Program.cs o deshabilitar los jobs en BD es suficiente para volver al estado anterior sin afectar ningún flujo activo.


## Componentes que NO se modifican en ninguna fase

Componente
Razón
ActionExecutorService / PayloadBuilder / HttpDispatcher
Los executors de Fases 2 y 3 los usan como caja negra. Sin modificaciones.
AnthropicAgentRunner
El clasificador de etiquetado llama a la API de Claude directamente, no al AgentRunner.
ISessionStore / Redis
Los jobs del Worker no tienen sesión de conversación activa.
Los 16 controllers existentes
Cero modificaciones.
Migraciones 1-31
Las nuevas migraciones son 32 y 33. Ninguna anterior se toca.

AgentFlow — Documento Confidencial  |  Campaign Automation Worker v2.0  |  Abril 2026

