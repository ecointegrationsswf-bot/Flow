---
name: deploy
description: Despliega el frontend y/o backend de AgentFlow/TalkIA a los servidores de produccion via FTP. Usa este skill cuando el usuario quiera publicar, deployar o subir cambios al servidor. Ejecuta build + upload FTP automaticamente.
---

# Deploy — AgentFlow / TalkIA

Este skill gestiona el proceso completo de build y publicacion del proyecto AgentFlow en los servidores de produccion SmartASP.

## Configuracion de servidores

### Frontend (site11) — React/Vite
- **FTP Host**: win1232.site4now.net:21
- **FTP Path**: /talkiav2app
- **Usuario**: jamconsulting-004
- **URL produccion**: http://jamconsulting-004-site11.site4future.com/
- **Build output**: `C:\TalkIA\frontend\dist`
- **Comando build**: `cd C:\TalkIA\frontend && npm run build`

### Backend (site12) — ASP.NET Core API
- **FTP Host**: win1232.site4now.net:21
- **FTP Path**: /talkiav2api
- **Usuario**: jamconsulting-004
- **URL produccion**: http://jamconsulting-004-site12.site4future.com/
- **Build output**: `C:\TalkIA\src\AgentFlow.API\bin\Release\net8.0\publish`
- **Comando build**: `dotnet publish C:\TalkIA\src\AgentFlow.API -c Release -o C:\TalkIA\src\AgentFlow.API\bin\Release\net8.0\publish`

## Proceso de deploy

Cuando el usuario invoca `/deploy`, seguir este flujo:

### 1. Solicitar password FTP
Preguntar la contrasena FTP del usuario jamconsulting-004 si no fue proporcionada.

### 2. Determinar que deployar
- `/deploy frontend` — solo frontend
- `/deploy backend` — solo backend
- `/deploy` o `/deploy all` — ambos (frontend primero, luego backend)

### 3. Build Frontend
```bash
cd C:\TalkIA\frontend
npm run build
```
Verificar que `dist/` fue generado correctamente. Si hay errores de TypeScript o build, reportarlos antes de subir.

### 4. Upload Frontend via FTP (PowerShell)
Usar el siguiente script PowerShell para subir el contenido de `dist/` al servidor:

```powershell
$ftpHost = "ftp://win1232.site4now.net"
$ftpPath = "/talkiav2app"
$user = "jamconsulting-004"
$pass = "<PASSWORD>"
$localPath = "C:\TalkIA\frontend\dist"

# Subir recursivamente todos los archivos del dist
function Upload-FTP($localDir, $ftpDir) {
    $items = Get-ChildItem $localDir
    foreach ($item in $items) {
        if ($item.PSIsContainer) {
            # Es carpeta — crear en FTP y recursar
            $newFtpDir = "$ftpDir/$($item.Name)"
            try {
                $req = [System.Net.FtpWebRequest]::Create("$ftpHost$newFtpDir")
                $req.Credentials = New-Object System.Net.NetworkCredential($user, $pass)
                $req.Method = [System.Net.WebRequestMethods+Ftp]::MakeDirectory
                $req.UsePassive = $true
                $req.GetResponse() | Out-Null
            } catch { }
            Upload-FTP $item.FullName $newFtpDir
        } else {
            # Es archivo — subir
            $ftpUri = "$ftpHost$ftpDir/$($item.Name)"
            $req = [System.Net.FtpWebRequest]::Create($ftpUri)
            $req.Credentials = New-Object System.Net.NetworkCredential($user, $pass)
            $req.Method = [System.Net.WebRequestMethods+Ftp]::UploadFile
            $req.UsePassive = $true
            $req.UseBinary = $true
            $req.ContentLength = $item.Length
            $fileStream = $item.OpenRead()
            $ftpStream = $req.GetRequestStream()
            $fileStream.CopyTo($ftpStream)
            $ftpStream.Close()
            $fileStream.Close()
            Write-Host "Subido: $($item.Name)"
        }
    }
}

Upload-FTP $localPath $ftpPath
```

### 5. Build Backend
```bash
dotnet publish C:\TalkIA\src\AgentFlow.API -c Release -o C:\TalkIA\src\AgentFlow.API\bin\Release\net8.0\publish --self-contained false
```

### 6. Upload Backend via FTP
Mismo script PowerShell pero con:
- `$ftpPath = "/talkiav2api"`
- `$localPath = "C:\TalkIA\src\AgentFlow.API\bin\Release\net8.0\publish"`

### 7. Verificacion post-deploy
- Abrir URL de frontend y confirmar que carga
- Hacer GET a `/health` o `/api/auth/ping` del backend para confirmar que responde

## Archivos a excluir del backend
Al publicar el backend, NO subir:
- `appsettings.Development.json` (contiene secrets locales)
- `*.pdb` (archivos de debug)

## Notas importantes
- El servidor SmartASP usa Windows Hosting, el backend debe publicarse para win-x64 o any
- El `appsettings.json` en produccion debe tener la cadena de conexion correcta al SQL Server de SmartASP
- Si es primer deploy, verificar que el Application Pool en el panel de SmartASP apunta a la carpeta correcta
- La password FTP NO se guarda en este archivo — pedirla al usuario en cada sesion de deploy
