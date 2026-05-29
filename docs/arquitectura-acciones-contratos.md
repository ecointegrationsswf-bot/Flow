# Arquitectura: Acciones → Contratos por Tenant

> Modelo acordado (Mayo 2026) para resolver el caos de "acciones duplicadas".
> La **acción** es un encabezado global único; el **contrato** (webhook) es por
> **tenant**. Editar el contrato de un tenant nunca pisa el de otro.

## Por qué

El diseño anterior "simulaba" el aislamiento por-tenant de dos formas sucias:

1. **Clonando la `ActionDefinition` entera por tenant** — el contrato vivía en el
   `DefaultWebhookContract` del clon. Genera duplicados en la lista de acciones y,
   si se borra el clon, se pierde el contrato (incidente PASESA 2FA, 2026-05-28).
2. **Metiéndolo en `CampaignTemplate.ActionConfigs`** (por template).

Dos mecanismos para lo mismo = inconsistencia. El modelo nuevo unifica: el contrato
como entidad propia por `(Acción × Tenant)`.

## Modelo de datos

```
        ╔══════════════════════════════════════════╗
        ║        ActionDefinition  (GLOBAL)         ║   ← encabezado único por slug
        ║──────────────────────────────────────────║      (NO se clona por tenant)
        ║  Id                                       ║
        ║  Name / slug      ej: NOTIFY_GESTION      ║
        ║  Description       "Enviar gestiones"     ║
        ║  RequiresWebhook                          ║
        ║  DefaultWebhookContract  ····················╫···► (opcional) plantilla / fallback
        ╚════════════════════╤═════════════════════╝
                             │ 1
                             │ N
                             ▼
   ╔════════════════════════════════════════════════════════════════╗
   ║                   TenantActionContract                          ║
   ║─────────────────────────────────────────────────────────────────║
   ║  Id                                                             ║
   ║  ActionDefinitionId   ──FK──►  ActionDefinition  (la acción)    ║
   ║  TenantId             ──FK──►  Tenant            (el dueño)     ║
   ║  WebhookUrl · WebhookMethod · ContentType · Structure          ║
   ║  AuthType (None|ApiKey|Bearer|Basic) · AuthValue · ApiKeyHeader ║
   ║  WebhookHeaders · TimeoutSeconds                               ║
   ║  InputSchema · OutputSchema · TriggerConfig · ChainRules (JSON) ║
   ║  IsActive · CreatedAt · UpdatedAt                              ║
   ║      ◆ UNIQUE (ActionDefinitionId, TenantId) ◆                  ║
   ╚════════════════════════════════════╤═══════════════════════════╝
                                         │ N
                                         │ 1
                                         ▼
                                ╔═════════════════╗
                                ║     Tenant      ║
                                ╚═════════════════╝
```

## Ejemplo — una acción, contratos distintos por tenant

```
                 ╔═══════════════════════════════╗
                 ║  NOTIFY_GESTION  (global)     ║
                 ╚═══╤═══════════════════╤════════╝
        ┌────────────┘                   └────────────┐
        ▼                                             ▼
╔═══════════════════════════╗            ╔═══════════════════════════╗
║ Contrato · AFTA           ║            ║ Contrato · PASESA         ║
║ URL: prmapisvc.afta...    ║            ║ URL: api.pasesa.../gestion║
║ Auth: Basic               ║            ║ Auth: ApiKey              ║
╚═══════════════════════════╝            ╚═══════════════════════════╝
   editar AFTA NO toca PASESA               editar PASESA NO toca AFTA
```

## Resolución en runtime

```
[ACTION:NOTIFY_GESTION] (tenant = AFTA)
   │
   ├─ 1) ActionDefinition global por slug "NOTIFY_GESTION"
   │
   ├─ 2) TenantActionContract(acción, tenant=AFTA)?
   │        SÍ → usar ese contrato
   │        NO → fallback a DefaultWebhookContract global (plantilla por defecto)
   │             (legacy: CampaignTemplate.ActionConfigs hasta terminar migración)
   │
   └─ 3) Ejecutar webhook con el contrato resuelto
```

El resolver es **aditivo**: si no hay `TenantActionContract`, el sistema se comporta
igual que antes (fallback). Por eso la introducción no rompe nada — recién cuando se
migran/crean contratos toman precedencia.

## Reglas

- **La acción NO se clona por tenant.** Una sola `ActionDefinition` global por slug.
- **Un contrato por `(Acción, Tenant)`** — UNIQUE. No se pisan entre tenants.
- **Granularidad: por tenant** (no por template). Todos los maestros del tenant usan
  el mismo contrato para esa acción.
- **Fallback** al `DefaultWebhookContract` global cuando el tenant no tiene contrato
  propio — sirve de plantilla.
- El endpoint de "asignar acciones a tenant" **ya NO clona** la acción; solo agrega
  el id global a `Tenant.AssignedActionIds`.

## Consumo (3 puntos)

La resolución del contrato se centraliza en `IActionContractResolver` y la consumen:
- `ActionExecutorService` — ejecuta el webhook.
- `ActionChainResolver` — lee `ChainRules` del contrato.
- `ActionPromptBuilder` — lee `TriggerConfig` para el catálogo del agente.
