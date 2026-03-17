# AgentFlow — Decisiones de arquitectura

## Monolito modular
Un solo deployment, múltiples módulos con fronteras claras.
Permite escalar hacia microservicios en el futuro extrayendo módulos.

## Multitenancy
- Cada Tenant tiene su número WhatsApp, proveedor (UltraMsg o Meta) y agentes.
- El TenantMiddleware resuelve el tenant desde JWT o header.
- Todas las queries filtran por TenantId — nunca hay data cross-tenant.

## Flujo de mensaje entrante
1. UltraMsg/Meta → POST /api/webhooks/message
2. TenantMiddleware resuelve tenant
3. ProcessIncomingMessageCommand → IContextDispatcher
4. Dispatcher: Redis (sesión activa) → BD (campaña activa) → LLM (intención)
5. IAgentRunner ejecuta el agente Claude correcto
6. Respuesta → IChannelProvider.SendMessageAsync
7. GestionEvent registrado → SignalR notify al monitor

## Sesiones en Redis
Clave: session:{tenantId}:{phone} — TTL: 72h
Resuelve el problema de TalkIA: al reconectar el sistema no pierde estado
y no relanza campañas ya procesadas.
