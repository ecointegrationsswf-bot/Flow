/**
 * Plantilla HTML sugerida para el cuerpo del correo del maestro de campaña.
 * El usuario hace click en "Cargar plantilla sugerida" en el tab Correo y se
 * pre-llena el editor. NO es un default automático del sistema — opt-in
 * explícito: si el usuario no la carga, el editor queda vacío y no se envía
 * correo (el backend valida que EmailBodyHtml no sea null/vacío).
 *
 * Variables consumidas (lo que alimenta el EmailTemplateRenderer):
 *   cliente.{nombre, telefono, email, items[], items_label, total_items, is_corporativo, saldo}
 *   item.{titulo, subtitulo, categoria, monto, detalles[].{k,v}}
 *   conversacion.{resumen, mensajes, estado}
 *   campana.nombre, agente.nombre, tenant.nombre, fecha, hora
 *
 * Las columnas reales del archivo del usuario se mapean a estos slots vía
 * CampaignTemplate.ItemsConfig (titleColumn → titulo, amountColumn → monto, etc.).
 */

export const SUGGESTED_EMAIL_SUBJECT =
  '{{ if cliente.is_corporativo }}Estado de cuenta consolidado · {{ cliente.total_items }} {{ cliente.items_label | string.downcase }}{{ else }}Tu estado de cuenta — {{ cliente.total_items }} {{ if cliente.total_items == 1 }}registro{{ else }}{{ cliente.items_label | string.downcase }}{{ end }}{{ end }}'

export const SUGGESTED_EMAIL_HTML = `<table role="presentation" cellpadding="0" cellspacing="0" width="640" style="max-width:640px;margin:0 auto;background:#ffffff;border:1px solid #e5e7eb;font-family:'Segoe UI',Arial,sans-serif;color:#0f172a">
<tr><td>

<!-- HERO -->
<table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td style="padding:16px 30px 14px;background:linear-gradient(135deg,#0f2547 0%,#1a3a6b 70%);color:#ffffff">
  <table width="100%"><tr>
    <td style="vertical-align:middle">
      {{ if tenant.logo != "" }}
        <img src="{{ tenant.logo }}" alt="{{ tenant.nombre }}" height="110" style="display:block;max-height:110px;width:auto;border:0;background:#ffffff;padding:8px 14px;border-radius:10px" />
      {{ else }}
        <div style="font-size:11px;letter-spacing:.12em;text-transform:uppercase;opacity:.85;margin-bottom:4px">Tu corredor de confianza</div>
        <div style="font-size:18px;font-weight:700;letter-spacing:.04em">{{ tenant.nombre | string.upcase }}</div>
      {{ end }}
    </td>
    <td align="right" style="font-size:11px;opacity:.85;text-align:right;vertical-align:middle">
      <div>Folio</div>
      <div style="font-weight:700;color:#ffffff">{{ campana.nombre }}</div>
      <div style="margin-top:4px">{{ fecha }} · {{ hora }}</div>
    </td>
  </tr></table>
  <h1 style="margin:10px 0 0;font-size:22px;font-weight:700;line-height:1.2;letter-spacing:-.01em">
    {{ if cliente.is_corporativo }}
      Estado de cuenta consolidado
    {{ else }}
      Tu estado de cuenta
    {{ end }}
  </h1>
</td></tr></table>

{{ if cliente.is_corporativo }}
<!-- KPIs corporativos -->
<table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#ffffff;border-bottom:1px solid #e5e7eb"><tr>
  <td style="padding:16px 18px;border-right:1px solid #f1f3f5;width:33%">
    <div style="font-size:10px;text-transform:uppercase;letter-spacing:.08em;color:#6b7280;font-weight:700;margin-bottom:4px">{{ cliente.items_label }} activas</div>
    <div style="font-size:22px;font-weight:800;color:#1a3a6b;line-height:1">{{ cliente.total_items }}</div>
  </td>
  <td style="padding:16px 18px;border-right:1px solid #f1f3f5;width:33%">
    <div style="font-size:10px;text-transform:uppercase;letter-spacing:.08em;color:#6b7280;font-weight:700;margin-bottom:4px">Saldo total</div>
    <div style="font-size:22px;font-weight:800;color:#b91c1c;line-height:1">{{ cliente.saldo }}</div>
  </td>
  <td style="padding:16px 18px;width:33%">
    <div style="font-size:10px;text-transform:uppercase;letter-spacing:.08em;color:#6b7280;font-weight:700;margin-bottom:4px">Estado</div>
    <div style="font-size:14px;font-weight:700;color:#0f172a;line-height:1.2">{{ conversacion.estado }}</div>
  </td>
</tr></table>
{{ else }}
<!-- Status bar individual -->
<table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr>
  <td style="padding:16px 30px;background:#fafbfc;border-bottom:1px solid #e5e7eb">
    <table width="100%"><tr>
      <td style="vertical-align:middle">
        <div style="display:inline-block;width:8px;height:8px;border-radius:50%;background:#10b981;margin-right:8px;vertical-align:middle"></div>
        <span style="font-size:11px;text-transform:uppercase;letter-spacing:.06em;color:#6b7280;font-weight:600">Estado:</span>
        <span style="font-size:13px;font-weight:700;color:#0f172a;margin-left:4px">{{ conversacion.estado }}</span>
      </td>
      <td align="right">
        <div style="font-size:10px;text-transform:uppercase;letter-spacing:.08em;color:#6b7280;font-weight:700">Saldo total</div>
        <div style="font-size:22px;font-weight:800;color:#b91c1c;letter-spacing:-.02em;line-height:1.1">{{ cliente.saldo }}</div>
      </td>
    </tr></table>
  </td>
</tr></table>
{{ end }}

<!-- CONTENT -->
<table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td style="padding:24px 30px">
  <div style="font-size:16px;font-weight:600;margin-bottom:6px">Hola {{ cliente.nombre }} 👋</div>
  <p style="color:#374151;font-size:13.5px;margin:0 0 14px;line-height:1.55">
    {{ if cliente.is_corporativo }}
      Adjuntamos el estado consolidado de sus <strong>{{ cliente.total_items }} {{ cliente.items_label | string.downcase }}</strong> activas con saldo pendiente:
    {{ else }}
      {{ if cliente.total_items > 1 }}
        Te dejamos el detalle de tus <strong>{{ cliente.total_items }} {{ cliente.items_label | string.downcase }}</strong> con saldo pendiente:
      {{ else }}
        Te dejamos el detalle de tu registro con saldo pendiente:
      {{ end }}
    {{ end }}
  </p>

  {{ if cliente.is_corporativo }}
  <!-- Lista compacta corporativa -->
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:12px 0">
    {{~ for it in cliente.items ~}}
    <tr><td style="padding:10px 14px;border:1px solid #e5e7eb;border-radius:8px;background:#fafbfc;margin-bottom:6px;display:block">
      <table width="100%"><tr>
        <td>
          {{ if it.categoria != "" }}<span style="display:inline-block;font-size:10px;padding:3px 9px;border-radius:6px;font-weight:800;letter-spacing:.06em;text-transform:uppercase;color:#ffffff;background:#1a3a6b;margin-right:8px">{{ it.categoria }}</span>{{ end }}
          <span style="font-size:13px;font-weight:700;color:#0f172a">{{ it.titulo }}</span>
          {{ if it.subtitulo != "" }}<span style="font-size:12px;color:#6b7280"> · {{ it.subtitulo }}</span>{{ end }}
        </td>
        <td align="right" style="font-size:15px;font-weight:800;color:#b91c1c;white-space:nowrap">{{ it.monto }}</td>
      </tr></table>
    </td></tr>
    {{~ end ~}}
  </table>
  {{ else }}
  <!-- Cards detallados (individual) — formato "estado de cuenta" con tabla densa -->
  {{~ for it in cliente.items ~}}
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:14px 0;border:1px solid #e5e7eb;border-radius:10px;overflow:hidden;background:#ffffff">
    <!-- Header de la póliza -->
    <tr><td style="padding:14px 18px;background:linear-gradient(180deg,#fbfcfe 0%,#f3f5f8 100%);border-bottom:1px solid #e5e7eb">
      <table width="100%"><tr>
        <td style="vertical-align:middle">
          {{ if it.categoria != "" }}<span style="display:inline-block;font-size:10px;padding:3px 10px;border-radius:999px;font-weight:800;letter-spacing:.06em;text-transform:uppercase;color:#ffffff;background:#1a3a6b;margin-right:8px;vertical-align:middle">{{ it.categoria }}</span>{{ end }}
          <span style="font-size:14px;font-weight:700;color:#0f172a;vertical-align:middle">{{ it.titulo }}</span>
          {{ if it.subtitulo != "" }}<div style="font-size:11px;color:#6b7280;margin-top:3px;letter-spacing:.02em">{{ it.subtitulo }}</div>{{ end }}
        </td>
        {{ if it.monto != "" }}
        <td align="right" style="vertical-align:top;white-space:nowrap;min-width:120px">
          <div style="font-size:10px;text-transform:uppercase;letter-spacing:.06em;color:#6b7280;font-weight:600">Saldo</div>
          <div style="font-size:20px;font-weight:800;color:#b91c1c;letter-spacing:-.01em;line-height:1.1">{{ it.monto }}</div>
        </td>
        {{ end }}
      </tr></table>
    </td></tr>

    {{~ if (array.size it.detalles) > 0 ~}}
    <!-- Tabla de detalle 2 columnas — formato "estado de cuenta" -->
    <tr><td style="padding:0">
      <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="border-collapse:collapse">
        {{~ for d in it.detalles ~}}
          {{~ if for.index % 2 == 0 ~}}
            <tr style="background:{{ if for.index % 4 == 0 }}#ffffff{{ else }}#fafbfc{{ end }}">
          {{~ end ~}}
              <td style="padding:9px 16px;border-bottom:1px solid #f1f3f5;width:25%;font-size:10px;text-transform:uppercase;letter-spacing:.05em;color:#6b7280;font-weight:700;vertical-align:middle">{{ d.k }}</td>
              <td style="padding:9px 16px;border-bottom:1px solid #f1f3f5;width:25%;font-size:12.5px;color:#0f172a;font-weight:600;vertical-align:middle">{{ d.v }}</td>
          {{~ if for.index % 2 == 1 || for.last ~}}
            {{~ if for.last && for.index % 2 == 0 ~}}
              <td style="padding:9px 16px;border-bottom:1px solid #f1f3f5;width:25%"></td>
              <td style="padding:9px 16px;border-bottom:1px solid #f1f3f5;width:25%"></td>
            {{~ end ~}}
            </tr>
          {{~ end ~}}
        {{~ end ~}}
      </table>
    </td></tr>
    {{~ end ~}}
  </table>
  {{~ end ~}}
  {{ end }}

  {{ if cliente.saldo != "" }}
  <!-- Total -->
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:18px 0">
    <tr><td style="background:#0f172a;color:#ffffff;border-radius:10px;padding:18px 22px">
      <table width="100%"><tr>
        <td>
          <div style="font-size:11px;text-transform:uppercase;letter-spacing:.1em;opacity:.7;font-weight:600">Total a pagar</div>
          <div style="font-size:28px;font-weight:800;letter-spacing:-.02em;line-height:1.1;margin-top:2px">{{ cliente.saldo }}</div>
        </td>
        <td align="right" style="font-size:11px;opacity:.85;line-height:1.55">
          <b style="color:#ffffff;font-size:12px">{{ cliente.total_items }} {{ cliente.items_label | string.downcase }}</b><br>
          {{ conversacion.estado }}
        </td>
      </tr></table>
    </td></tr>
  </table>
  {{ end }}

  {{ if conversacion.resumen != "" }}
  <!-- Resumen conversación -->
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin:14px 0"><tr>
    <td style="padding:12px 14px;background:#fafbfc;border-left:3px solid #1a3a6b;border-radius:0 6px 6px 0;font-size:12.5px;color:#374151">
      <div style="font-size:10px;font-weight:700;color:#6b7280;text-transform:uppercase;letter-spacing:.05em;margin-bottom:4px">
        Resumen de la gestión con {{ agente.nombre }}
      </div>
      {{ conversacion.resumen | html.escape }}
    </td>
  </tr></table>
  {{ end }}

  <p style="margin:16px 0 0;font-size:13px;color:#374151;line-height:1.55">
    Si ya realizaste el pago, puedes ignorar este correo. Para cualquier consulta responde este mensaje y un asesor te atenderá a la brevedad.
  </p>

  {{ if cliente.ejecutivo.nombre != "" || cliente.ejecutivo.email != "" || cliente.ejecutivo.telefono != "" }}
  <!-- Firma del ejecutivo asignado -->
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="margin-top:24px;border-top:1px solid #e5e7eb;padding-top:14px"><tr>
    <td>
      <div style="font-size:10px;font-weight:700;text-transform:uppercase;letter-spacing:.08em;color:#6b7280;margin-bottom:6px">
        Tu ejecutivo de cobros
      </div>
      {{ if cliente.ejecutivo.nombre != "" }}<div style="font-size:14px;font-weight:700;color:#0f172a">{{ cliente.ejecutivo.nombre }}</div>{{ end }}
      {{ if cliente.ejecutivo.email != "" }}<div style="font-size:12.5px;color:#374151;margin-top:3px">✉ <a href="mailto:{{ cliente.ejecutivo.email }}" style="color:#1a3a6b;text-decoration:none">{{ cliente.ejecutivo.email }}</a></div>{{ end }}
      {{ if cliente.ejecutivo.telefono != "" }}<div style="font-size:12.5px;color:#374151;margin-top:2px">☎ <a href="tel:+{{ cliente.ejecutivo.telefono }}" style="color:#1a3a6b;text-decoration:none">{{ cliente.ejecutivo.telefono }}</a></div>{{ end }}
    </td>
  </tr></table>
  {{ end }}

</td></tr></table>

<!-- FOOTER -->
<table role="presentation" width="100%" cellpadding="0" cellspacing="0"><tr><td style="background:#0f172a;color:#cbd5e1;padding:20px 30px;font-size:11px;line-height:1.65">
  <div style="font-size:14px;font-weight:700;color:#ffffff;margin-bottom:4px">{{ tenant.nombre }}</div>
  <div>Gestionado por <strong style="color:#ffffff">{{ agente.nombre }}</strong> · Campaña: {{ campana.nombre }}</div>
  <div style="color:#64748b;font-size:10px;line-height:1.65;padding-top:12px;border-top:1px solid #1e293b;margin-top:10px">
    Mensaje automatizado. Si recibiste este correo por error, ignoralo. Para hablar con un ejecutivo respondé este mensaje.
  </div>
</td></tr></table>

</td></tr>
</table>`
