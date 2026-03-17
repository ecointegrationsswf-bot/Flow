# AgentFlow

Plataforma multitenante de agentes IA para gestión de cobros, reclamos y renovaciones de seguros vía WhatsApp, Email y SMS.

## Stack
- **Backend**: ASP.NET Core 8 — Monolito modular
- **Frontend**: React 18 + TypeScript + Vite + Tailwind
- **LLM**: Claude API (Anthropic) — claude-sonnet-4-6
- **WhatsApp**: UltraMsg (onboarding rápido) + Meta Cloud API (enterprise)
- **Orquestación**: n8n (self-hosted)
- **BD**: SQL Server 2022
- **Sesiones**: Redis
- **Tiempo real**: SignalR
- **Jobs**: Hangfire

## Estructura de la solución

```
AgentFlow.sln
├── src/
│   ├── AgentFlow.Domain/          # Entidades, interfaces, enums — sin dependencias
│   │   ├── Entities/
│   │   ├── Enums/
│   │   ├── Interfaces/
│   │   └── ValueObjects/
│   ├── AgentFlow.Application/     # Casos de uso — MediatR commands/queries
│   │   └── Modules/
│   │       ├── Webhooks/          # ProcessIncomingMessageCommand
│   │       ├── Campaigns/         # StartCampaignCommand
│   │       ├── Monitor/           # GetActiveConversationsQuery
│   │       └── Agents/            # CRUD de agentes IA
│   ├── AgentFlow.Infrastructure/  # EF Core, Redis, UltraMsg, Anthropic SDK
│   │   ├── Persistence/
│   │   ├── Channels/
│   │   │   ├── UltraMsg/
│   │   │   └── MetaCloudApi/
│   │   ├── AI/                    # AnthropicAgentRunner
│   │   └── Session/               # RedisSessionStore
│   └── AgentFlow.API/             # Controllers, SignalR Hub, Middleware
│       ├── Controllers/
│       ├── Hubs/
│       └── Middleware/
└── frontend/                      # React + Vite
    └── src/
        ├── modules/
        │   ├── monitor/           # Monitor en vivo (SignalR)
        │   ├── campaigns/         # Carga de campañas
        │   └── agents/            # Configuración de agentes
        └── shared/
            ├── api/               # Axios client
            └── hooks/             # useConversationHub (SignalR)
```

## Inicio rápido

```bash
# Backend
cd src/AgentFlow.API
cp appsettings.json appsettings.Development.json
# Editar connection strings y API keys
dotnet run

# Frontend
cd frontend
npm install
npm run dev
```

## Migraciones EF Core

```bash
cd src/AgentFlow.API
dotnet ef migrations add InitialCreate --project ../AgentFlow.Infrastructure
dotnet ef database update
```
