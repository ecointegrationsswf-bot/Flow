// Genera el Word: Propuesta de integración Meta WhatsApp Cloud API multitenant (aditiva).
// Uso: node generate-meta-multitenant.js  (NODE_PATH al global de npm si docx es global)
// Salida: ./Propuesta-Meta-WhatsApp-Multitenant.docx

const fs = require('fs');
const path = require('path');
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  AlignmentType, LevelFormat, HeadingLevel, BorderStyle, WidthType, ShadingType,
  PageNumber, Header, Footer,
} = require('docx');

const NAVY = '1A3A6B', ACCENT = '2E75B6', GREEN = '047857', AMBER = 'D97706', RED = 'B91C1C', GRAY_BG = 'F1F5F9', CALLOUT = 'EAF2FB', BORDER = 'CCCCCC';
const bd = { style: BorderStyle.SINGLE, size: 1, color: BORDER };
const borders = { top: bd, bottom: bd, left: bd, right: bd };

const t = (text, o = {}) => new TextRun({ text, ...o });
const p = (children, o = {}) => new Paragraph({ children, ...o });
const plain = (text, o = {}) => p([t(text)], o);
const b = (text) => t(text, { bold: true });
const code = (text) => t(text, { font: 'Consolas', size: 18 });
const h1 = (text) => new Paragraph({ heading: HeadingLevel.HEADING_1, children: [t(text)], spacing: { before: 320, after: 160 } });
const h2 = (text) => new Paragraph({ heading: HeadingLevel.HEADING_2, children: [t(text)], spacing: { before: 240, after: 120 } });
const bullet = (children) => new Paragraph({ numbering: { reference: 'bul', level: 0 }, children });
const numbered = (children) => new Paragraph({ numbering: { reference: 'num', level: 0 }, children });
const cell = (children, o = {}) => new TableCell({
  borders, width: { size: o.width ?? 3120, type: WidthType.DXA },
  shading: o.shading ? { fill: o.shading, type: ShadingType.CLEAR } : undefined,
  margins: { top: 90, bottom: 90, left: 130, right: 130 },
  children: Array.isArray(children) ? children : [children],
});
const hcell = (text, w) => cell(p([t(text, { bold: true, color: 'FFFFFF' })]), { width: w, shading: NAVY });
const codeBlock = (lines) => lines.map(line => new Paragraph({
  children: [code(line.length ? line : ' ')], shading: { fill: GRAY_BG, type: ShadingType.CLEAR }, spacing: { before: 0, after: 0 },
}));
const callout = (title, paras) => new Table({
  width: { size: 9360, type: WidthType.DXA }, columnWidths: [9360],
  borders: { top: { style: BorderStyle.SINGLE, size: 1, color: ACCENT }, bottom: { style: BorderStyle.SINGLE, size: 1, color: ACCENT }, left: { style: BorderStyle.SINGLE, size: 18, color: ACCENT }, right: { style: BorderStyle.SINGLE, size: 1, color: ACCENT } },
  rows: [new TableRow({ children: [new TableCell({ width: { size: 9360, type: WidthType.DXA }, shading: { fill: CALLOUT, type: ShadingType.CLEAR }, margins: { top: 120, bottom: 120, left: 180, right: 160 }, children: [p([t(title, { bold: true, color: NAVY, size: 20 })], { spacing: { after: 80 } }), ...paras] })] })],
});

const C = [];

// PORTADA
C.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 2200, after: 200 }, children: [t('Integración Meta WhatsApp Cloud API', { bold: true, size: 44, color: NAVY })] }));
C.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 200 }, children: [t('Proveedor multitenant aditivo para TalkIA', { bold: true, size: 30, color: ACCENT })] }));
C.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 600 }, border: { bottom: { style: BorderStyle.SINGLE, size: 12, color: ACCENT, space: 1 } }, children: [t(' ')] }));
C.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 100 }, children: [t('Análisis de flujo y esfuerzo de construcción', { size: 24, color: '475569' })] }));
C.push(new Paragraph({ alignment: AlignmentType.CENTER, children: [t('AgentFlow / TalkIA — Junio 2026', { size: 22, color: '64748B' })] }));
C.push(new Paragraph({ children: [t(' ')], pageBreakBefore: true }));

// 1. RESUMEN
C.push(h1('1. Resumen ejecutivo'));
C.push(plain('TalkIA hoy opera WhatsApp exclusivamente vía UltraMsg (conexión por QR, no oficial). Este documento analiza cómo incorporar Meta WhatsApp Cloud API (canal oficial) como un segundo proveedor seleccionable por tenant, de forma estrictamente aditiva: el flujo UltraMsg actual no se toca y cada cliente puede seguir en UltraMsg o migrar a Meta sin afectar a los demás.'));
C.push(plain('El administrador elige el proveedor por tenant/línea. La buena noticia: ~60% de la arquitectura ya existe. La interfaz IChannelProvider y sus 12+ puntos de envío están listos; el enum ProviderType {UltraMsg, MetaCloudApi} y el campo WhatsAppLine.Provider ya están en el modelo. Lo que falta se concentra en tres frentes solicitados: (1) parametrizar credenciales Meta por tenant desde el portal admin, (2) crear y solicitar aprobación de plantillas desde el tenant del cliente, y (3) un webhook único de entrada multi-tenant, análogo al de UltraMsg.'));
C.push(callout('Principio rector — Aditivo y seleccionable por tenant', [
  bullet([b('No se toca UltraMsg.'), t(' El proveedor por defecto sigue siendo UltraMsg; un deploy no altera el flujo vivo de los tenants actuales.')]),
  bullet([b('El admin elige.'), t(' Por tenant/línea se selecciona el proveedor (UltraMsg | MetaCloudApi). El agente IA no sabe por cuál canal sale el mensaje.')]),
  bullet([b('Reutiliza la abstracción.'), t(' Los 12+ call-sites de envío ya pasan por IChannelProvider; solo el factory necesita ramificar por proveedor.')]),
]));

// 2. ESTADO ACTUAL
C.push(h1('2. Estado actual — qué ya está y qué falta'));
const tEstado = new Table({ width: { size: 9360, type: WidthType.DXA }, columnWidths: [4000, 1600, 3760], rows: [
  new TableRow({ tableHeader: true, children: [hcell('Componente', 4000), hcell('Estado', 1600), hcell('Nota', 3760)] }),
  new TableRow({ children: [cell(plain('Interfaz IChannelProvider (Send / Status / ValidateSignature)'), { width: 4000 }), cell(p([t('LISTO', { bold: true, color: GREEN })]), { width: 1600 }), cell(plain('Usada por 12+ puntos de envío (dispatcher, follow-ups, auto-close, respuesta humana, etc.).'), { width: 3760 })] }),
  new TableRow({ children: [cell(plain('Enum ProviderType {UltraMsg, MetaCloudApi}'), { width: 4000 }), cell(p([t('LISTO', { bold: true, color: GREEN })]), { width: 1600 }), cell(plain('Ya existe en Domain.Enums.'), { width: 3760 })] }),
  new TableRow({ children: [cell(plain('WhatsAppLine.Provider (por línea/tenant)'), { width: 4000 }), cell(p([t('LISTO', { bold: true, color: GREEN })]), { width: 1600 }), cell(plain('Campo persistido; default UltraMsg.'), { width: 3760 })] }),
  new TableRow({ children: [cell(plain('ChannelProviderFactory.BuildProvider'), { width: 4000 }), cell(p([t('AJUSTE', { bold: true, color: AMBER })]), { width: 1600 }), cell(plain('Hoy hardcodeado a UltraMsgProvider; ignora line.Provider. Debe ramificar por proveedor.'), { width: 3760 })] }),
  new TableRow({ children: [cell(plain('MetaCloudApiProvider (IChannelProvider)'), { width: 4000 }), cell(p([t('FALTA', { bold: true, color: RED })]), { width: 1600 }), cell(plain('No existe. Enviar texto/plantilla, status, validar firma HMAC.'), { width: 3760 })] }),
  new TableRow({ children: [cell(plain('Credenciales Meta por tenant (WABA, Phone ID, token, app secret)'), { width: 4000 }), cell(p([t('FALTA', { bold: true, color: RED })]), { width: 1600 }), cell(plain('WhatsAppLine solo tiene InstanceId/ApiToken (UltraMsg). Faltan campos Meta + UI admin.'), { width: 3760 })] }),
  new TableRow({ children: [cell(plain('Gestión de plantillas por tenant (crear/aprobar/estado)'), { width: 4000 }), cell(p([t('FALTA', { bold: true, color: RED })]), { width: 1600 }), cell(plain('No existe entidad ni UI. Es el frente más grande.'), { width: 3760 })] }),
  new TableRow({ children: [cell(plain('Webhook único multi-tenant para Meta'), { width: 4000 }), cell(p([t('FALTA', { bold: true, color: RED })]), { width: 1600 }), cell(plain('Existe el de UltraMsg como referencia (resuelve tenant por InstanceId). Meta necesita el suyo: GET verify + firma HMAC + resolución por phone_number_id.'), { width: 3760 })] }),
]});
C.push(tEstado);

// 3. ARQUITECTURA
C.push(h1('3. Arquitectura propuesta'));
C.push(plain('La selección de proveedor vive en la línea WhatsApp del tenant. El factory deja de asumir UltraMsg y ramifica según line.Provider. Todo lo aguas arriba (agente, dispatcher, follow-ups) queda intacto.'));
C.push(h2('3.1. Selección de proveedor (envío)'));
C.push(...codeBlock([
  '  Dispatcher / Follow-up / Respuesta humana',
  '        │  providerFactory.GetProviderAsync(tenantId)',
  '        ▼',
  '  ChannelProviderFactory',
  '        │  lee WhatsAppLine activa → line.Provider',
  '        ├── UltraMsg      → UltraMsgProvider      (HOY, sin cambios)',
  '        └── MetaCloudApi  → MetaCloudApiProvider  (NUEVO)',
  '        ▼',
  '  IChannelProvider.SendMessageAsync(...)  ← el agente no sabe cuál es',
]));
C.push(h2('3.2. Webhook único de entrada (recepción) — igual que UltraMsg'));
C.push(...codeBlock([
  '  Meta Cloud  ──POST (firma X-Hub-Signature-256)──►  /api/webhooks/meta',
  '                                                          │',
  '   1) GET verify (hub.challenge) al registrar el webhook  │',
  '   2) Validar firma HMAC-SHA256 con App Secret del tenant │',
  '   3) Resolver tenant por value.metadata.phone_number_id  │',
  '   4) Normalizar payload Meta → formato interno           ▼',
  '        ├── mensaje entrante → DispatchAsync (igual que hoy)',
  '        └── status (sent/delivered/read/failed) → actualizar Message',
]));

// 4. FRENTE 1
C.push(h1('4. Frente 1 — Credenciales Meta por tenant (portal admin)'));
C.push(plain('Meta requiere por número de negocio un set de datos que hoy no se guarda. Se parametriza por tenant/línea desde el portal admin, cifrando los secretos.'));
const tCreds = new Table({ width: { size: 9360, type: WidthType.DXA }, columnWidths: [2600, 4000, 2760], rows: [
  new TableRow({ tableHeader: true, children: [hcell('Dato', 2600), hcell('Para qué', 4000), hcell('Sensibilidad', 2760)] }),
  new TableRow({ children: [cell(p([code('phone_number_id')]), { width: 2600 }), cell(plain('Enviar mensajes y resolver el tenant en el webhook.'), { width: 4000 }), cell(plain('Identificador (no secreto)'), { width: 2760 })] }),
  new TableRow({ children: [cell(p([code('waba_id')]), { width: 2600 }), cell(plain('Crear/listar plantillas y suscripción de webhook.'), { width: 4000 }), cell(plain('Identificador'), { width: 2760 })] }),
  new TableRow({ children: [cell(p([code('access_token')]), { width: 2600 }), cell(plain('Autenticación de todas las llamadas al Graph API.'), { width: 4000 }), cell(p([t('SECRETO (cifrar)', { bold: true, color: RED })]), { width: 2760 })] }),
  new TableRow({ children: [cell(p([code('app_secret')]), { width: 2600 }), cell(plain('Validar la firma HMAC del webhook entrante.'), { width: 4000 }), cell(p([t('SECRETO (cifrar)', { bold: true, color: RED })]), { width: 2760 })] }),
  new TableRow({ children: [cell(p([code('business_id')]), { width: 2600 }), cell(plain('Referencia del portfolio (verificación del negocio).'), { width: 4000 }), cell(plain('Identificador'), { width: 2760 })] }),
  new TableRow({ children: [cell(p([code('verify_token')]), { width: 2600 }), cell(plain('Handshake GET del webhook (lo define TalkIA).'), { width: 4000 }), cell(plain('Semi-secreto'), { width: 2760 })] }),
]});
C.push(tCreds);
C.push(plain('Dónde guardarlo (aditivo): extender WhatsAppLine con los campos Meta nullable (o una tabla hija MetaLineConfig 1:1). Tokens cifrados con Data Protection API. UltraMsg sigue usando InstanceId/ApiToken; las líneas Meta dejan esos vacíos y llenan los suyos.'));
C.push(plain('UI admin: en la pantalla de líneas WhatsApp del tenant, un selector de proveedor; si es Meta, formulario con esos campos (sin QR). Validación: un "probar conexión" que pega a Graph API /{phone_number_id} (health_status) antes de guardar.'));

// 5. FRENTE 2
C.push(h1('5. Frente 2 — Plantillas por tenant (crear y solicitar aprobación)'));
C.push(plain('A diferencia de UltraMsg, Meta exige plantillas pre-aprobadas para iniciar conversaciones en frío (fuera de la ventana de 24h). Cada tenant gestiona las suyas desde su portal.'));
C.push(h2('5.1. Ciclo de vida de una plantilla'));
C.push(...codeBlock([
  '  Crear en TalkIA  ──POST /{waba_id}/message_templates──►  Meta',
  '        │                                                   │',
  '        ▼                                                   ▼',
  '  Estado PENDING ──(Meta revisa: min/horas)──► APPROVED / REJECTED',
  '        ▲                                                   │',
  '        └── sync de estado (polling o webhook de templates) ┘',
  '  APPROVED → usable en campañas (type=template, variables {{1}}..{{n}})',
]));
C.push(h2('5.2. Qué construir'));
C.push(bullet([b('Entidad WhatsAppTemplate (nueva): '), t('TenantId, name, language, category, status, components (JSON), ejemplos, providerTemplateId, rejectedReason, timestamps.')]));
C.push(bullet([b('Endpoints: '), t('crear (POST a Meta + persistir PENDING), listar por tenant, refrescar estado, eliminar.')]));
C.push(bullet([b('UI por tenant: '), t('editor de plantilla (cuerpo + variables + ejemplos), botón "enviar a aprobación", badge de estado (PENDING/APPROVED/REJECTED con motivo).')]));
C.push(bullet([b('Integración con campañas: '), t('al lanzar una campaña en una línea Meta, elegir plantilla APPROVED y mapear cada {{n}} a una columna del archivo (nombre, póliza, saldo…). Aquí se reusa el patrón de variables que ya existe en los follow-ups ({nombre}, {poliza}, {monto_pendiente}).')]));
C.push(plain('Nota operativa: el mensaje inicial de campaña pasa a ser una plantilla; una vez el cliente responde, el agente IA continúa en texto libre dentro de la ventana de 24h (sin cambios en el flujo conversacional).'));

// 6. FRENTE 3
C.push(h1('6. Frente 3 — Webhook único multi-tenant'));
C.push(plain('Un solo endpoint para TODOS los tenants Meta, igual que el de UltraMsg resuelve por InstanceId. Diferencias propias de Meta:'));
C.push(numbered([b('Verificación GET: '), t('al registrar el webhook, Meta hace GET con hub.mode/hub.verify_token/hub.challenge; hay que responder el challenge.')]));
C.push(numbered([b('Firma HMAC: '), t('cada POST trae X-Hub-Signature-256 = HMAC-SHA256(body, app_secret). Validar antes de procesar (ValidateWebhookSignature ya está en la interfaz).')]));
C.push(numbered([b('Resolución de tenant: '), t('por value.metadata.phone_number_id → buscar la WhatsAppLine Meta con ese id (índice único recomendado).')]));
C.push(numbered([b('Normalización: '), t('mapear el payload Meta (entry[].changes[].value.messages / .statuses) al formato interno que ya consume DispatchAsync, e idempotencia por message id (wamid).')]));
C.push(numbered([b('Statuses: '), t('Meta envía sent/delivered/read/failed por webhook (UltraMsg usa ACK 0..3). Mapear a MessageStatus.')]));
C.push(callout('Reutiliza la lección del incidente 140984', [
  plain('El resolver del webhook UltraMsg usaba FirstOrDefault sin orden y con InstanceId duplicado entre tenants mezcló conversaciones. Para Meta: imponer índice ÚNICO en phone_number_id por línea activa y resolución determinista, para que un entrante nunca caiga en el tenant equivocado.'),
]));

// 7. DIFERENCIAS
C.push(h1('7. Diferencias clave: Meta vs UltraMsg'));
const tDiff = new Table({ width: { size: 9360, type: WidthType.DXA }, columnWidths: [3120, 3120, 3120], rows: [
  new TableRow({ tableHeader: true, children: [hcell('Aspecto', 3120), hcell('UltraMsg (hoy)', 3120), hcell('Meta Cloud API', 3120)] }),
  new TableRow({ children: [cell(plain('Conexión'), { width: 3120 }), cell(plain('QR scan, número existente'), { width: 3120 }), cell(plain('Alta oficial, sin QR; verificación de negocio'), { width: 3120 })] }),
  new TableRow({ children: [cell(plain('Inicio en frío'), { width: 3120 }), cell(plain('Texto libre directo'), { width: 3120 }), cell(p([t('Solo plantillas aprobadas', { bold: true })])  , { width: 3120 })] }),
  new TableRow({ children: [cell(plain('Ventana 24h'), { width: 3120 }), cell(plain('No aplica'), { width: 3120 }), cell(plain('Texto libre solo dentro de 24h tras respuesta del cliente'), { width: 3120 })] }),
  new TableRow({ children: [cell(plain('Webhook entrante'), { width: 3120 }), cell(plain('Wrapper UltraMsg, sin firma'), { width: 3120 }), cell(plain('GET verify + firma HMAC-SHA256'), { width: 3120 })] }),
  new TableRow({ children: [cell(plain('Resolución de tenant'), { width: 3120 }), cell(plain('InstanceId'), { width: 3120 }), cell(plain('phone_number_id'), { width: 3120 })] }),
  new TableRow({ children: [cell(plain('Estados de entrega'), { width: 3120 }), cell(plain('ACK -1..3'), { width: 3120 }), cell(plain('sent / delivered / read / failed'), { width: 3120 })] }),
  new TableRow({ children: [cell(plain('Salud de línea'), { width: 3120 }), cell(plain('Ping /instance/status + QR'), { width: 3120 }), cell(plain('health_status del Graph API (sin QR)'), { width: 3120 })] }),
  new TableRow({ children: [cell(plain('Riesgo de ban'), { width: 3120 }), cell(plain('Alto en volumen (no oficial)'), { width: 3120 }), cell(p([t('Bajo (oficial)', { bold: true, color: GREEN })]), { width: 3120 })] }),
]});
C.push(tDiff);

// 8. PLAN POR FASES + ESFUERZO
C.push(h1('8. Plan de construcción y esfuerzo'));
C.push(plain('Estimación para 1 desarrollador. Las fases A–C dejan envío Meta funcional; D–E completan plantillas y entrada; F es endurecimiento y cutover. Rango por incertidumbre de revisión de Meta y UI.'));
const tEsf = new Table({ width: { size: 9360, type: WidthType.DXA }, columnWidths: [800, 4400, 1480, 2680], rows: [
  new TableRow({ tableHeader: true, children: [hcell('Fase', 800), hcell('Alcance', 4400), hcell('Esfuerzo', 1480), hcell('Depende de', 2680)] }),
  new TableRow({ children: [cell(p([b('A')]), { width: 800 }), cell(plain('Modelo de datos: campos Meta en WhatsAppLine (o MetaLineConfig) + cifrado + migración aditiva.'), { width: 4400 }), cell(plain('2–3 días'), { width: 1480 }), cell(plain('—'), { width: 2680 })] }),
  new TableRow({ children: [cell(p([b('B')]), { width: 800 }), cell(plain('MetaCloudApiProvider: enviar texto + plantilla, status, ValidateWebhookSignature. + ramificar BuildProvider por line.Provider.'), { width: 4400 }), cell(plain('3–4 días'), { width: 1480 }), cell(plain('A'), { width: 2680 })] }),
  new TableRow({ children: [cell(p([b('C')]), { width: 800 }), cell(plain('Portal admin: selector de proveedor por línea + formulario de credenciales Meta + "probar conexión".'), { width: 4400 }), cell(plain('3–5 días'), { width: 1480 }), cell(plain('A, B'), { width: 2680 })] }),
  new TableRow({ children: [cell(p([b('D')]), { width: 800 }), cell(plain('Plantillas: entidad WhatsAppTemplate + endpoints (crear/listar/estado) + UI editor + sync de aprobación + uso en campañas (mapeo de variables).'), { width: 4400 }), cell(p([t('5–8 días', { bold: true })]), { width: 1480 }), cell(plain('A, B'), { width: 2680 })] }),
  new TableRow({ children: [cell(p([b('E')]), { width: 800 }), cell(plain('Webhook único /api/webhooks/meta: GET verify + HMAC + resolución por phone_number_id + normalización inbound/statuses + idempotencia + índice único.'), { width: 4400 }), cell(plain('4–6 días'), { width: 1480 }), cell(plain('A, B'), { width: 2680 })] }),
  new TableRow({ children: [cell(p([b('F')]), { width: 800 }), cell(plain('Pruebas E2E en sandbox, salud de línea Meta, documentación y cutover por tenant.'), { width: 4400 }), cell(plain('3–4 días'), { width: 1480 }), cell(plain('B–E'), { width: 2680 })] }),
  new TableRow({ children: [cell(p([b('Total')]), { width: 800, shading: GRAY_BG }), cell(p([b('Integración completa (aditiva, sin tocar UltraMsg)')]), { width: 4400, shading: GRAY_BG }), cell(p([t('≈ 4–6 semanas', { bold: true, color: NAVY })]), { width: 1480, shading: GRAY_BG }), cell(plain(''), { width: 2680, shading: GRAY_BG })] }),
]});
C.push(tEsf);
C.push(plain('Atajo posible: las fases A+B+C+E (sin el módulo de plantillas D) ya permiten responder/recibir y enviar plantillas creadas manualmente en el panel de Meta — útil para un piloto rápido. El módulo D (plantillas autogestionadas por el tenant) puede ir en una segunda iteración.'));

// 9. RIESGOS
C.push(h1('9. Riesgos y mitigaciones'));
const tR = new Table({ width: { size: 9360, type: WidthType.DXA }, columnWidths: [3120, 6240], rows: [
  new TableRow({ tableHeader: true, children: [hcell('Riesgo', 3120), hcell('Mitigación', 6240)] }),
  new TableRow({ children: [cell(plain('Aprobación de plantillas lenta o rechazos de Meta.'), { width: 3120 }), cell(plain('UI que muestra estado y motivo de rechazo; biblioteca de plantillas base ya validadas (recordatorio de pago UTILITY).'), { width: 6240 })] }),
  new TableRow({ children: [cell(plain('Verificación de negocio pendiente limita el envío.'), { width: 3120 }), cell(plain('Checklist de onboarding por tenant (business verification, display name, tier de 250/día) antes de activar el proveedor.'), { width: 6240 })] }),
  new TableRow({ children: [cell(plain('Webhook resuelve el tenant equivocado (como el incidente 140984).'), { width: 3120 }), cell(plain('Índice ÚNICO en phone_number_id por línea activa + resolución determinista + validación de firma.'), { width: 6240 })] }),
  new TableRow({ children: [cell(plain('Manejo de secretos (token/app_secret) en BD.'), { width: 3120 }), cell(plain('Cifrado con Data Protection API; nunca en logs; rotación documentada.'), { width: 6240 })] }),
  new TableRow({ children: [cell(plain('Regresión en el flujo UltraMsg.'), { width: 3120 }), cell(plain('Todo es aditivo: default UltraMsg, ramas nuevas solo se ejecutan si line.Provider=MetaCloudApi. Pruebas de no-regresión + cutover por tenant.'), { width: 6240 })] }),
]});
C.push(tR);

// 10. CONCLUSION
C.push(h1('10. Conclusión y próximos pasos'));
C.push(plain('La integración es viable y mayormente aditiva: la abstracción de canal y el modelo (ProviderType, WhatsAppLine.Provider) ya están. El esfuerzo se concentra en el provider Meta, la parametrización por tenant, el módulo de plantillas y el webhook único. Nada de esto altera el flujo UltraMsg vivo, y el administrador elige el proveedor por cliente.'));
C.push(plain('Próximos pasos sugeridos:'));
C.push(bullet([t('Validar el alcance y priorizar piloto (A+B+C+E) vs. integración completa con plantillas autogestionadas (D).')]));
C.push(bullet([t('Definir dónde guardar credenciales Meta: campos en WhatsAppLine vs. tabla MetaLineConfig.')]));
C.push(bullet([t('Acordar el dominio público del webhook único y el verify_token.')]));
C.push(bullet([t('Completar verificación de negocio y display name del primer tenant piloto en Meta.')]));
C.push(new Paragraph({ spacing: { before: 500 }, border: { top: { style: BorderStyle.SINGLE, size: 6, color: ACCENT, space: 8 } }, children: [t(' ')] }));
C.push(new Paragraph({ alignment: AlignmentType.CENTER, children: [t('Documento técnico — AgentFlow / TalkIA — Junio 2026', { italics: true, color: '64748B', size: 18 })] }));

const doc = new Document({
  creator: 'AgentFlow', title: 'Integración Meta WhatsApp Cloud API multitenant (aditiva)',
  description: 'Análisis de flujo y esfuerzo para incorporar Meta Cloud API como proveedor por tenant',
  styles: { default: { document: { run: { font: 'Arial', size: 22 } } }, paragraphStyles: [
    { id: 'Heading1', name: 'Heading 1', basedOn: 'Normal', next: 'Normal', quickFormat: true, run: { size: 30, bold: true, font: 'Arial', color: NAVY }, paragraph: { spacing: { before: 340, after: 160 }, outlineLevel: 0 } },
    { id: 'Heading2', name: 'Heading 2', basedOn: 'Normal', next: 'Normal', quickFormat: true, run: { size: 25, bold: true, font: 'Arial', color: ACCENT }, paragraph: { spacing: { before: 220, after: 110 }, outlineLevel: 1 } },
  ] },
  numbering: { config: [
    { reference: 'bul', levels: [{ level: 0, format: LevelFormat.BULLET, text: '•', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 720, hanging: 360 } } } }] },
    { reference: 'num', levels: [{ level: 0, format: LevelFormat.DECIMAL, text: '%1.', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 720, hanging: 360 } } } }] },
  ] },
  sections: [{
    properties: { page: { size: { width: 12240, height: 15840 }, margin: { top: 1440, right: 1440, bottom: 1440, left: 1440 } } },
    headers: { default: new Header({ children: [new Paragraph({ alignment: AlignmentType.RIGHT, children: [t('Meta WhatsApp multitenant — TalkIA', { italics: true, size: 18, color: '64748B' })] })] }) },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER, children: [t('AgentFlow / TalkIA  •  Página ', { size: 18, color: '64748B' }), new TextRun({ size: 18, color: '64748B', children: [PageNumber.CURRENT] })] })] }) },
    children: C,
  }],
});

const outPath = path.join(__dirname, 'Propuesta-Meta-WhatsApp-Multitenant.docx');
Packer.toBuffer(doc).then(buf => { fs.writeFileSync(outPath, buf); console.log('OK ->', outPath); }).catch(e => { console.error('ERROR:', e); process.exit(1); });
