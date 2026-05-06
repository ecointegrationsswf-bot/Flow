---
name: frontend-design
description: Create distinctive, production-grade frontend interfaces with high design quality. Use this skill when the user asks to build web components, pages, artifacts, posters, or applications (examples include websites, landing pages, dashboards, React components, HTML/CSS layouts, or when styling/beautifying any web UI). Generates creative, polished code and UI design that avoids generic AI aesthetics.
license: Complete terms in LICENSE.txt
---

This skill guides creation of distinctive, production-grade frontend interfaces that avoid generic "AI slop" aesthetics. Implement real working code with exceptional attention to aesthetic details and creative choices.

The user provides frontend requirements: a component, page, application, or interface to build. They may include context about the purpose, audience, or technical constraints.

## Design Thinking

Before coding, understand the context and commit to a BOLD aesthetic direction:
- **Purpose**: What problem does this interface solve? Who uses it?
- **Tone**: Pick an extreme: brutally minimal, maximalist chaos, retro-futuristic, organic/natural, luxury/refined, playful/toy-like, editorial/magazine, brutalist/raw, art deco/geometric, soft/pastel, industrial/utilitarian, etc. There are so many flavors to choose from. Use these for inspiration but design one that is true to the aesthetic direction.
- **Constraints**: Technical requirements (framework, performance, accessibility).
- **Differentiation**: What makes this UNFORGETTABLE? What's the one thing someone will remember?

**CRITICAL**: Choose a clear conceptual direction and execute it with precision. Bold maximalism and refined minimalism both work - the key is intentionality, not intensity.

Then implement working code (HTML/CSS/JS, React, Vue, etc.) that is:
- Production-grade and functional
- Visually striking and memorable
- Cohesive with a clear aesthetic point-of-view
- Meticulously refined in every detail

## Frontend Aesthetics Guidelines

Focus on:
- **Typography**: Choose fonts that are beautiful, unique, and interesting. Avoid generic fonts like Arial and Inter; opt instead for distinctive choices that elevate the frontend's aesthetics; unexpected, characterful font choices. Pair a distinctive display font with a refined body font.
- **Color & Theme**: Commit to a cohesive aesthetic. Use CSS variables for consistency. Dominant colors with sharp accents outperform timid, evenly-distributed palettes.
- **Motion**: Use animations for effects and micro-interactions. Prioritize CSS-only solutions for HTML. Use Motion library for React when available. Focus on high-impact moments: one well-orchestrated page load with staggered reveals (animation-delay) creates more delight than scattered micro-interactions. Use scroll-triggering and hover states that surprise.
- **Spatial Composition**: Unexpected layouts. Asymmetry. Overlap. Diagonal flow. Grid-breaking elements. Generous negative space OR controlled density.
- **Backgrounds & Visual Details**: Create atmosphere and depth rather than defaulting to solid colors. Add contextual effects and textures that match the overall aesthetic. Apply creative forms like gradient meshes, noise textures, geometric patterns, layered transparencies, dramatic shadows, decorative borders, custom cursors, and grain overlays.

NEVER use generic AI-generated aesthetics like overused font families (Inter, Roboto, Arial, system fonts), cliched color schemes (particularly purple gradients on white backgrounds), predictable layouts and component patterns, and cookie-cutter design that lacks context-specific character.

Interpret creatively and make unexpected choices that feel genuinely designed for the context. No design should be the same. Vary between light and dark themes, different fonts, different aesthetics. NEVER converge on common choices (Space Grotesk, for example) across generations.

**IMPORTANT**: Match implementation complexity to the aesthetic vision. Maximalist designs need elaborate code with extensive animations and effects. Minimalist or refined designs need restraint, precision, and careful attention to spacing, typography, and subtle details. Elegance comes from executing the vision well.

Remember: Claude is capable of extraordinary creative work. Don't hold back, show what can truly be created when thinking outside the box and committing fully to a distinctive vision.

## Reglas obligatorias del proyecto TalkIA / AgentFlow

### Fechas y horas — usar SIEMPRE `useTenantTime`

Toda fecha u hora visible en la UI debe formatearse con el hook `useTenantTime`
(o sus utilidades equivalentes en `shared/utils/tenantTime.ts`). El hook resuelve
la zona horaria del tenant activo (`Tenant.TimeZone`) con fallback silencioso a
`America/Panama` si está nulo o malformado.

**PROHIBIDO** usar directamente cualquiera de estos en una pantalla:

```ts
new Date(iso).toLocaleString(...)        // ❌ usa TZ del navegador
new Date(iso).toLocaleDateString(...)    // ❌
new Date(iso).toLocaleTimeString(...)    // ❌
new Intl.DateTimeFormat(...).format(...) // ❌ (excepto dentro del propio hook)
format(new Date(iso), 'dd/MM/yyyy')      // ❌ date-fns sin TZ del tenant
formatDistanceToNow(new Date(iso), ...)  // ❌ usa locale del browser
```

**CORRECTO**:

```tsx
import { useTenantTime } from '@/shared/hooks/useTenantTime'

const tt = useTenantTime()

// Formatos disponibles:
tt.time(iso)            // "3:45 p. m."
tt.date(iso)            // "01/05/2026"
tt.dateShort(iso)       // "06/05"
tt.dateLong(iso)        // "5 de mayo de 2026"
tt.dateTime(iso)        // "01/05/2026 15:23"
tt.dateTimeShort(iso)   // "06/05, 12:57"
tt.monthYear(iso)       // "may. 2026"
tt.relative(iso)        // "hace 5 min", "hace 2 h", "hace 3 d"
tt.isToday(iso)         // boolean — mismo día calendario en TZ del tenant
tt.isYesterday(iso)     // boolean
tt.timeZone             // "America/Panama" (string actual)
tt.label                // { offset: "GMT-5", city: "Panama" } — para badges
```

Si necesitás un formato nuevo (ej: "Q2 2026", "semana 18"), agregalo al
hook — NO lo inlinees en la pantalla.

### Por qué esta regla existe

El frontend de TalkIA atiende tenants en distintos husos horarios. Si una pantalla
formatea con `toLocaleString()` sin pasar `timeZone`, usa la zona del navegador
del usuario — que rara vez coincide con la del tenant. Resultado: el ejecutivo
ve "Mensaje a las 3:20 a.m." cuando en realidad fue a las 22:20 PA del día
anterior, lo confunde sobre qué día filtrar, y nos pega bugs imposibles de
reproducir hasta que alguien abre desde un equipo en otro huso.

El hook centraliza la lógica en un único punto. Cuando un tenant nuevo se
configure en `America/Lima` o `America/Mexico_City`, todas las pantallas
quedan correctas sin tocar código.

### Cómo enviar fechas al backend

Datos que viajan al backend (POST/PUT) deben ir en **ISO 8601 UTC** —
`new Date(input).toISOString()` está OK porque no es display, es payload.
El servidor SIEMPRE almacena en UTC; la conversión a TZ del tenant ocurre
exclusivamente en el frontend al renderizar.
