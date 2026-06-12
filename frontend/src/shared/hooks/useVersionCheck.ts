import { useEffect, useState } from 'react'

/**
 * Detecta automáticamente cuando se deployó una nueva versión del frontend
 * mientras el usuario tenía la pestaña abierta.
 *
 * Mecánica:
 *  1. Al montar el hook, lee el href del script principal cargado en el HTML
 *     actual (típicamente "/assets/index-XXXX.js"). Ese es el "bundle vigente".
 *  2. Cada 60 segundos hace fetch a "/index.html" con cache:'no-store' y parsea
 *     el href del script principal del HTML del servidor.
 *  3. Si difieren, hay una nueva versión deployada → devuelve hasNewVersion=true
 *     para que la UI muestre un banner "Recargar".
 *
 * Como /index.html en producción tiene `Cache-Control: no-cache, no-store, must-revalidate`
 * (configurado en web.config), el fetch siempre trae la versión actual del servidor.
 * Los bundles /assets/*.js tienen hash en el nombre y caché 1 año immutable, así que
 * cuando el usuario hace reload, el browser descarga el bundle nuevo automáticamente.
 *
 * NO hace polling agresivo (60s es conservador). El usuario puede seguir trabajando
 * y el banner aparece cuando quiera; nunca se interrumpe lo que estaba haciendo.
 */
// Polling de 30s: balance entre responsividad y costo (1 fetch a /index.html
// ~2 KB cada 30 segundos por sesión).
const POLL_INTERVAL_MS = 30_000

function extractScriptSrcFromHtml(html: string): string | null {
  // El index.html generado por Vite contiene exactamente UN <script type="module" src="/assets/index-XXXX.js">.
  // Lo buscamos con regex tolerante a comillas simples/dobles y atributos extra.
  const match = html.match(/<script[^>]+type=["']module["'][^>]+src=["']([^"']+)["']/i)
  return match ? match[1] : null
}

function getCurrentBundleSrc(): string | null {
  // Lee del DOM actual el src del módulo principal. Esto representa el bundle
  // que el usuario tiene cargado en memoria — la versión "vigente" desde su POV.
  const scripts = document.querySelectorAll<HTMLScriptElement>('script[type="module"][src]')
  for (const s of scripts) {
    const src = s.getAttribute('src')
    if (src && src.includes('/assets/index-')) return src
  }
  return null
}

export function useVersionCheck() {
  const [hasNewVersion, setHasNewVersion] = useState(false)

  useEffect(() => {
    const currentSrc = getCurrentBundleSrc()
    if (!currentSrc) {
      // No hay bundle hasheado (probablemente en dev / vite hmr) — desactivar el check.
      return
    }

    let cancelled = false

    const check = async () => {
      try {
        const resp = await fetch(`/index.html?t=${Date.now()}`, {
          cache: 'no-store',
          credentials: 'omit',
        })
        if (!resp.ok) return
        const html = await resp.text()
        const serverSrc = extractScriptSrcFromHtml(html)
        if (!serverSrc) return
        if (!cancelled && serverSrc !== currentSrc) {
          setHasNewVersion(true)
        }
      } catch {
        // red caída / offline / abort — ignorar silenciosamente; reintentaremos al próximo tick.
      }
    }

    const interval = window.setInterval(check, POLL_INTERVAL_MS)
    // Check INMEDIATO al montar: cubre "entro al portal" con una pestaña restaurada por el
    // navegador (session restore) — antes el primer check era recién al tick de los 30s y
    // el usuario veía la versión vieja, creyendo que necesitaba hard refresh.
    void check()
    // Primer check al volver al tab (ej: usuario tenía la pestaña en background mientras deployamos).
    const onVisibility = () => { if (document.visibilityState === 'visible') void check() }
    document.addEventListener('visibilitychange', onVisibility)
    // bfcache (botón atrás / reabrir pestaña cerrada): el navegador restaura la página desde
    // memoria SIN tocar la red ni disparar visibilitychange — pageshow con persisted=true es
    // el único evento que lo detecta. Sin esto, la app vieja revive silenciosamente.
    const onPageShow = (e: PageTransitionEvent) => { if (e.persisted) void check() }
    window.addEventListener('pageshow', onPageShow)
    // Alt-tab de vuelta a la ventana (la pestaña nunca dejó de estar "visible").
    const onFocus = () => { void check() }
    window.addEventListener('focus', onFocus)

    return () => {
      cancelled = true
      window.clearInterval(interval)
      document.removeEventListener('visibilitychange', onVisibility)
      window.removeEventListener('pageshow', onPageShow)
      window.removeEventListener('focus', onFocus)
    }
  }, [])

  return hasNewVersion
}
