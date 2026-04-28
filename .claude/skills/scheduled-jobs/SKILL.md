---
name: scheduled-jobs
description: Procedimiento detallado para crear, modificar y reprogramar los Scheduled Jobs (ScheduledWebhookJobs) de AgentFlow/TalkIA — cron en hora Panamá, NextRunAt en UTC, y wiring con ActionDefinitions. Usa este skill cuando el usuario quiera crear, editar, reprogramar, pausar o ejecutar manualmente un job cron del ScheduledWebhookWorker (LABEL_CONVERSATIONS, SEND_LABELING_SUMMARY, NOTIFY_GESTION u otros).
---

# Scheduled Jobs — AgentFlow / TalkIA

Procedimiento completo para crear y modificar los **ScheduledWebhookJobs** que el `ScheduledWebhookWorker` monitorea cada **60s**.

> El worker se levanta como `BackgroundService` dentro del API (`Program.cs:150`). Si el API está corriendo, el worker también — no se levanta por separado.

---

## Conceptos clave

| Concepto | Detalle |
|---------|--------|
| Tabla | `ScheduledWebhookJobs` |
| FK | `ActionDefinitionId` → `ActionDefinitions.Id` (matching por `ActionDefinitions.Name` = slug) |
| Slug ejemplos | `LABEL_CONVERSATIONS`, `SEND_LABELING_SUMMARY`, `NOTIFY_GESTION` |
| Worker | `src/AgentFlow.Infrastructure/ScheduledJobs/ScheduledWebhookWorker.cs` (tick 60s) |
| TimeZone cron | **Hora Panamá (UTC-5)** — interpretado por `Cronos` con `PanamaTimeZone.Instance` |
| Campo `NextRunAt` | **Siempre UTC** en BD. Para 7:50 PA → `12:50 UTC`. |
| TriggerType | `Cron` \| `EventBased` \| `DelayFromEvent` |
| Scope | `AllTenants` \| `PerCampaign` \| `PerConversation` |

### Formato cron
`<min> <hour> <day-of-month> <month> <day-of-week>` — 5 campos estilo Unix.
Ejemplo: `50 7 * * *` = todos los días a las 7:50 hora Panamá.

### NextRunAt — regla de oro
Si vas a **cambiar la `CronExpression`**, **debes recalcular** `NextRunAt` también.
Si solo cambias el cron y dejas el `NextRunAt` viejo, el worker disparará en el horario antiguo y luego sí usará el cron nuevo. Para que el cambio aplique inmediatamente: setear `NextRunAt` a la próxima ocurrencia del cron nuevo (en UTC).

Conversión rápida: **PA = UTC − 5h**. Ejemplo: 8:00 PA = 13:00 UTC.

---

## Vías para crear/modificar jobs

Hay **3 vías** ordenadas por preferencia:

### Vía 1 — UI Admin (preferida si está disponible)
Ruta: `/admin/scheduled-jobs` en el frontend.
- Botón **"+ Nuevo job"** abre el editor.
- Botón ▶ ejecuta manualmente (encola con `TriggeredBy="Manual"`).
- Botón 🕒 muestra historial.
- Botón 🗑 elimina.

Ventaja: valida cron con `POST /api/scheduled-jobs/preview-cron`, recalcula `NextRunAt` automáticamente.
Limitación: requiere autenticación admin.

### Vía 2 — API REST (`ScheduledJobsController`)
Endpoints en `src/AgentFlow.API/Controllers/ScheduledJobsController.cs`:

| Método | Endpoint | Acción |
|--------|----------|--------|
| GET    | `/api/scheduled-jobs` | Listar |
| GET    | `/api/scheduled-jobs/{id}` | Obtener uno |
| POST   | `/api/scheduled-jobs` | Crear |
| PUT    | `/api/scheduled-jobs/{id}` | Actualizar (recalcula `NextRunAt` si TriggerType=Cron) |
| DELETE | `/api/scheduled-jobs/{id}` | Eliminar |
| POST   | `/api/scheduled-jobs/{id}/run-now` | Ejecutar manual |
| GET    | `/api/scheduled-jobs/{id}/executions?take=N` | Historial |
| POST   | `/api/scheduled-jobs/preview-cron` | Validar cron + ver próximas 5 ocurrencias |

Body de crear/actualizar (`ScheduledJobUpsertRequest`):
```json
{
  "actionDefinitionId": "<guid>",
  "triggerType": "Cron",
  "cronExpression": "50 7 * * *",
  "triggerEvent": null,
  "delayMinutes": null,
  "scope": "AllTenants",
  "isActive": true
}
```

### Vía 3 — SQL directo (cuando no hay sesión admin)
Útil para entornos sin acceso a la UI o para batch updates rápidos. **Atención**: hay que recalcular `NextRunAt` manualmente.

Connection string: `src/AgentFlow.API/appsettings.Development.json` → `ConnectionStrings:DefaultConnection`.

Ejecutar con `sqlcmd`:
```bash
"/c/Program Files/Microsoft SQL Server/Client SDK/ODBC/170/Tools/Binn/sqlcmd" \
  -S "tcp:sql1003.site4now.net,1433" \
  -d "db_ab2fbb_flow" \
  -U "db_ab2fbb_flow_admin" \
  -P "<password>" \
  -N -C -i ".claude/tmp/apply-cron.sql"
```

---

## Plantillas SQL listas para usar

### Crear un job nuevo
```sql
DECLARE @ActionId UNIQUEIDENTIFIER = (
    SELECT Id FROM ActionDefinitions WHERE Name = 'LABEL_CONVERSATIONS'
);

INSERT INTO ScheduledWebhookJobs
    (Id, ActionDefinitionId, TriggerType, CronExpression, TriggerEvent,
     DelayMinutes, Scope, IsActive, NextRunAt, CreatedAt, UpdatedAt,
     ConsecutiveFailures)
VALUES
    (NEWID(), @ActionId, 'Cron', '50 7 * * *', NULL,
     NULL, 'AllTenants', 1,
     '2026-04-29 12:50:00',  -- UTC = 7:50 PA del próximo día
     SYSUTCDATETIME(), SYSUTCDATETIME(), 0);
```

### Modificar el cron de un job existente (por slug)
```sql
UPDATE swj
   SET CronExpression = '50 7 * * *',     -- nuevo cron en hora PA
       TriggerType    = 'Cron',
       IsActive       = 1,
       NextRunAt      = '2026-04-28 12:50:00', -- UTC = PA - 5h
       UpdatedAt      = SYSUTCDATETIME()
  FROM ScheduledWebhookJobs swj
  JOIN ActionDefinitions ad ON ad.Id = swj.ActionDefinitionId
 WHERE ad.Name = 'LABEL_CONVERSATIONS';
```

### Pausar / reactivar
```sql
UPDATE swj SET IsActive = 0, UpdatedAt = SYSUTCDATETIME()
  FROM ScheduledWebhookJobs swj
  JOIN ActionDefinitions ad ON ad.Id = swj.ActionDefinitionId
 WHERE ad.Name = 'NOTIFY_GESTION';
```

### Forzar ejecución inmediata
```sql
UPDATE swj
   SET NextRunAt = SYSUTCDATETIME(),
       LastRunStatus = NULL,
       UpdatedAt = SYSUTCDATETIME()
  FROM ScheduledWebhookJobs swj
  JOIN ActionDefinitions ad ON ad.Id = swj.ActionDefinitionId
 WHERE ad.Name = 'LABEL_CONVERSATIONS';
```
> El worker en su próximo tick (≤60s) verá `NextRunAt <= now`, lo recogerá y marcará como `Running`. Tras terminar, computará el próximo `NextRunAt` desde `CronExpression`.

### Verificar estado
```sql
SELECT ad.Name,
       swj.CronExpression,
       swj.NextRunAt,            -- UTC
       swj.IsActive,
       swj.LastRunStatus,        -- Pending|Running|Success|Failed
       swj.LastRunAt,            -- UTC
       swj.LastRunSummary,
       swj.ConsecutiveFailures
  FROM ScheduledWebhookJobs swj
  JOIN ActionDefinitions ad ON ad.Id = swj.ActionDefinitionId
 ORDER BY swj.NextRunAt;
```

### Reset de etiquetas (para que el labeling re-procese todo)
```sql
UPDATE Conversations
   SET LabelId = NULL,
       LabeledAt = NULL,
       LabelingResultJson = NULL
 WHERE LabelId IS NOT NULL OR LabeledAt IS NOT NULL OR LabelingResultJson IS NOT NULL;
```

---

## Conversión PA → UTC (cheat sheet)

| Hora PA | Hora UTC | Cron PA |
|---------|----------|---------|
| 06:00   | 11:00    | `0 6 * * *`  |
| 07:50   | 12:50    | `50 7 * * *` |
| 08:00   | 13:00    | `0 8 * * *`  |
| 08:10   | 13:10    | `10 8 * * *` |
| 13:31   | 18:31    | `31 13 * * *`|
| 22:00   | 03:00 (+1d) | `0 22 * * *` |

> Panamá no usa horario de verano: el offset siempre es −5h.

---

## Flujo operativo: programar 3 jobs en secuencia (caso típico)

Ejemplo: etiquetar a las 7:50, enviar resumen a las 8:00, notificar gestión a las 8:10 (todos PA, hoy).

1. Calcular UTC de cada hora (PA + 5h).
2. Limpiar etiquetas si se quiere re-etiquetar todo (SQL del bloque "Reset de etiquetas").
3. Update por slug:
   - `LABEL_CONVERSATIONS` → `50 7 * * *`, `NextRunAt = HOY 12:50 UTC`
   - `SEND_LABELING_SUMMARY` → `0 8 * * *`, `NextRunAt = HOY 13:00 UTC`
   - `NOTIFY_GESTION` → `10 8 * * *`, `NextRunAt = HOY 13:10 UTC`
4. Verificar con el SELECT del bloque "Verificar estado".
5. Confirmar que el worker está corriendo:
   ```bash
   # buscar en los logs del API
   grep "ScheduledWebhookWorker iniciado" <log>
   ```
   o desde preview_logs si el API se levantó con `preview_start`.

---

## Validación de cron antes de aplicar

Endpoint público (con auth):
```http
POST /api/scheduled-jobs/preview-cron
{ "expression": "50 7 * * *" }
```
Devuelve `{ valid: true, nextOccurrencesUtc: [...] }` con las próximas 5 ejecuciones en UTC, ya interpretadas en hora Panamá.

Si no hay acceso al API, validar localmente con la sintaxis estándar Cronos: 5 campos, sin segundos.

---

## Errores comunes y cómo evitarlos

| Síntoma | Causa | Fix |
|---------|-------|-----|
| Cambio de cron no se aplica hasta mañana | `NextRunAt` quedó con el horario viejo | Setear `NextRunAt` a la próxima ocurrencia del cron nuevo en UTC |
| Worker no recoge el job | `IsActive = 0` o `LastRunStatus = 'Running'` huérfano | Activar y resetear `LastRunStatus` a NULL |
| Cron interpretado en UTC en vez de PA | Editaste por debajo del API y olvidaste que `NextRunAt` es UTC | Restar 5h al horario PA al calcular `NextRunAt` |
| `LABEL_CONVERSATIONS` no etiqueta nada | Conversación ya tiene `LabelId` y `LastActivityAt <= LabeledAt` | Reset SQL (ver bloque) |
| Job marcado `Running` indefinidamente | Falla sin manejo limpio | Worker tiene `ResetStuckRunningAsync` con cutoff; o forzar manual: `UPDATE ScheduledWebhookJobs SET LastRunStatus = NULL WHERE Id = ...` |

---

## Archivos de referencia

| Archivo | Rol |
|---------|-----|
| `src/AgentFlow.Domain/Entities/ScheduledWebhookJob.cs` | Entidad |
| `src/AgentFlow.Infrastructure/ScheduledJobs/ScheduledWebhookWorker.cs` | BackgroundService (tick 60s) |
| `src/AgentFlow.Infrastructure/ScheduledJobs/PanamaTimeZone.cs` | TimeZoneInfo usado por Cronos |
| `src/AgentFlow.Infrastructure/Persistence/Repositories/ScheduledJobRepository.cs` | Queries del worker |
| `src/AgentFlow.API/Controllers/ScheduledJobsController.cs` | API REST |
| `src/AgentFlow.Infrastructure/ScheduledJobs/ConversationLabelingJob.cs` | Executor `LABEL_CONVERSATIONS` |
| `src/AgentFlow.Infrastructure/ScheduledJobs/SendLabelingSummaryExecutor.cs` | Executor `SEND_LABELING_SUMMARY` |
| `src/AgentFlow.Infrastructure/ScheduledJobs/NotifyGestionBatchExecutor.cs` | Executor `NOTIFY_GESTION` |
| `src/AgentFlow.Infrastructure/ScheduledJobs/CampaignAutomationSeeder.cs` | Crea jobs por defecto al iniciar |
