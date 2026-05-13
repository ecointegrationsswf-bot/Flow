---
name: talkia-deploy
description: Convención de deploy de TalkIA/AgentFlow. TODOS los publishes (api, frontend, worker) se generan en C:\TalkIADeploy\<componente>\. Usa este skill cuando el usuario pida publicar/deployar/empaquetar cualquier componente del proyecto, o cuando pregunte dónde están los archivos a subir al servidor. NO publicar fuera de C:\TalkIADeploy.
---

# Convención de deploy — TalkIA / AgentFlow

## Regla de oro

**Todos los artefactos de publish viven en `C:\TalkIADeploy\`**, jamás en el working tree del proyecto (`C:\TalkIA\`).

```
C:\TalkIADeploy\
├── api\         ← AgentFlow.API publicado (ASP.NET Core)
├── frontend\    ← frontend/dist (Vite build)
└── worker\      ← AgentFlow.Worker publicado (Windows Service)
```

Esto:
- Mantiene `C:\TalkIA` limpio (solo source).
- Hace los deploys reproducibles (`rm -rf` y republish, sin tocar el repo).
- Permite zip + RDP/FTP sin riesgo de subir bin/obj de dev.

## Comandos por componente

### Worker (servicio Windows)
```powershell
.\scripts\deploy-worker.ps1
# → C:\TalkIADeploy\worker\
```
Para subir y deployar en un solo paso (FTP):
```powershell
.\scripts\deploy-worker.ps1 -UploadFtp -FtpPassword "<pass>"
```

### API (ASP.NET Core)
```powershell
dotnet publish src\AgentFlow.API -c Release -o C:\TalkIADeploy\api --self-contained false
# luego eliminar appsettings.Development.json del output
Remove-Item C:\TalkIADeploy\api\appsettings.Development.json -ErrorAction SilentlyContinue
```

### Frontend (React/Vite)
```powershell
cd frontend
npm run build
# Vite genera frontend\dist — copiarlo o symlinkearlo al deploy folder:
Remove-Item C:\TalkIADeploy\frontend -Recurse -Force -ErrorAction SilentlyContinue
Copy-Item -Path frontend\dist -Destination C:\TalkIADeploy\frontend -Recurse
```

## Reglas de publish

1. **Limpiar antes de publicar**: el script borra `C:\TalkIADeploy\<componente>\` antes de cada publish.
2. **Eliminar siempre `appsettings.Development.json`** del deploy output (tiene secrets de dev).
3. **Mantener `appsettings.json`** en el deploy (configurar overrides en producción vía `appsettings.Production.json` que se mantiene en el servidor entre deploys).
4. **Borrar `*.pdb`** opcional para deploy más liviano (los scripts lo permiten).
5. **No subir** `bin/`, `obj/`, ni nada de fuera de `C:\TalkIADeploy\`.

## Después del publish

| Componente | Cómo se sube al servidor | Cómo se activa |
|------------|--------------------------|----------------|
| Worker     | ZIP + RDP (o FTP) → `C:\AgentFlow\Worker\` | `install-worker-service.ps1` (1ra vez) o `update-worker-service.ps1` (updates) |
| API        | FTP → `/talkiav2api` (SmartASP) | Reiniciar AppPool del IIS |
| Frontend   | FTP → `/talkiav2app` (SmartASP) | Inmediato (estático) |

## Estructura limpia esperada en `C:\TalkIA`

```
C:\TalkIA\
├── .claude/        ← config Claude Code
├── .git/
├── .github/
├── .gitignore
├── .mcp.json
├── AgentFlow.sln
├── CLAUDE.md
├── README.md
├── docs/
├── frontend/       ← code (sin dist/, va a C:\TalkIADeploy\frontend)
├── n8n/
├── scripts/        ← deploy + service scripts
│   ├── deploy-worker.ps1
│   ├── install-worker-service.ps1
│   ├── uninstall-worker-service.ps1
│   ├── update-worker-service.ps1
│   └── legacy/     ← scripts viejos (preservados pero no se usan)
├── sql/            ← scripts SQL one-off (migraciones manuales, fixes)
├── src/            ← AgentFlow.API, .Application, .Domain, .Infrastructure, .Worker
└── tests/
```

NO debe haber:
- `publish/`, `TalkIApublish*/` (van a `C:\TalkIADeploy\`)
- `node_modules/` en root (solo en `frontend/`)
- `bin/`, `obj/` (gitignored)
- `*.log`, `*.pdf` sueltos
- `*.sql` o `*.ps1` sueltos en root (van a `sql/` o `scripts/`)

## Cuando el usuario pregunte "dónde están los archivos para deploy"

Responder con la ruta `C:\TalkIADeploy\<componente>\` correspondiente. Si no se ha publicado aún, ejecutar el script de deploy para generarlo.

## Cuando el usuario pida un nuevo deploy

1. Verificar que está en `C:\TalkIA` (working tree limpio).
2. Ejecutar el script de deploy del componente.
3. Confirmar que `C:\TalkIADeploy\<componente>\` quedó actualizado.
4. Recordar el siguiente paso (FTP/RDP/install-service según componente).
