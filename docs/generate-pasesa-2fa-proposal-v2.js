// Genera el documento Word (v2, Junio 2026) con la propuesta de simplificación
// del endpoint 2FA para el broker PASESA.
// Refresca la versión de Mayo 2026 y agrega un recuadro de "estado verificado
// en producción" (datos confirmados contra la BD el 2026-06-02).
// Uso: node generate-pasesa-2fa-proposal-v2.js
// Salida: ./Propuesta-Simplificacion-2FA-PASESA-Jun2026.docx

const fs = require('fs');
const path = require('path');

const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  AlignmentType, LevelFormat, HeadingLevel,
  BorderStyle, WidthType, ShadingType,
  PageNumber, Header, Footer,
} = require('docx');

// ---- estilos / colores ----
const NAVY = '1A3A6B';
const ACCENT = '2E75B6';
const GREEN = '047857';
const AMBER = 'D97706';
const RED = 'B91C1C';
const GRAY_BG = 'F1F5F9';
const CALLOUT_BG = 'EAF2FB';
const GRAY_BORDER = 'CCCCCC';

const border = { style: BorderStyle.SINGLE, size: 1, color: GRAY_BORDER };
const borders = { top: border, bottom: border, left: border, right: border };

// helpers ----------------------------------------------------------
const t = (text, opts = {}) => new TextRun({ text, ...opts });
const p = (children, opts = {}) => new Paragraph({ children, ...opts });
const plain = (text, opts = {}) => p([t(text)], opts);
const bold = (text) => t(text, { bold: true });
const code = (text) => t(text, { font: 'Consolas', size: 18 });

const h1 = (text) => new Paragraph({
  heading: HeadingLevel.HEADING_1,
  children: [t(text)],
  spacing: { before: 320, after: 160 },
});
const h2 = (text) => new Paragraph({
  heading: HeadingLevel.HEADING_2,
  children: [t(text)],
  spacing: { before: 240, after: 120 },
});

const bullet = (children) => new Paragraph({
  numbering: { reference: 'bullets', level: 0 },
  children,
});
const numbered = (children) => new Paragraph({
  numbering: { reference: 'numbers', level: 0 },
  children,
});

// Cell helper
const cell = (children, opts = {}) => new TableCell({
  borders,
  width: { size: opts.width ?? 4680, type: WidthType.DXA },
  shading: opts.shading ? { fill: opts.shading, type: ShadingType.CLEAR } : undefined,
  margins: { top: 100, bottom: 100, left: 140, right: 140 },
  children: Array.isArray(children) ? children : [children],
});

const headerCell = (text, width) => cell(
  p([t(text, { bold: true, color: 'FFFFFF' })]),
  { width, shading: NAVY },
);

// Code block (mono)
const codeBlock = (lines) => lines.map(line =>
  new Paragraph({
    children: [code(line.length ? line : ' ')],
    shading: { fill: GRAY_BG, type: ShadingType.CLEAR },
    spacing: { before: 0, after: 0 },
  })
);

// Recuadro tipo "callout" (verificación en producción)
const calloutBox = (titleText, paragraphs) => new Table({
  width: { size: 9360, type: WidthType.DXA },
  columnWidths: [9360],
  borders: {
    top: { style: BorderStyle.SINGLE, size: 1, color: ACCENT },
    bottom: { style: BorderStyle.SINGLE, size: 1, color: ACCENT },
    left: { style: BorderStyle.SINGLE, size: 18, color: ACCENT },
    right: { style: BorderStyle.SINGLE, size: 1, color: ACCENT },
  },
  rows: [new TableRow({
    children: [new TableCell({
      width: { size: 9360, type: WidthType.DXA },
      shading: { fill: CALLOUT_BG, type: ShadingType.CLEAR },
      margins: { top: 120, bottom: 120, left: 180, right: 160 },
      children: [
        p([t(titleText, { bold: true, color: NAVY, size: 20 })], { spacing: { after: 80 } }),
        ...paragraphs,
      ],
    })],
  })],
});

// ----- contenido del documento ------------------------------------
const children = [];

// PORTADA
children.push(new Paragraph({
  alignment: AlignmentType.CENTER,
  spacing: { before: 2400, after: 200 },
  children: [t('Propuesta de Simplificación', { bold: true, size: 48, color: NAVY })],
}));
children.push(new Paragraph({
  alignment: AlignmentType.CENTER,
  spacing: { after: 200 },
  children: [t('Endpoints 2FA — Broker PASESA', { bold: true, size: 36, color: ACCENT })],
}));
children.push(new Paragraph({
  alignment: AlignmentType.CENTER,
  spacing: { after: 600 },
  border: { bottom: { style: BorderStyle.SINGLE, size: 12, color: ACCENT, space: 1 } },
  children: [t(' ')],
}));
children.push(new Paragraph({
  alignment: AlignmentType.CENTER,
  spacing: { after: 100 },
  children: [t('AgentFlow / TalkIA', { size: 24, color: '475569' })],
}));
children.push(new Paragraph({
  alignment: AlignmentType.CENTER,
  spacing: { after: 100 },
  children: [t('Documento técnico — Junio 2026 (rev. 2)', { size: 22, color: '64748B' })],
}));
children.push(new Paragraph({ children: [t(' ')], pageBreakBefore: true }));

// 1. RESUMEN EJECUTIVO
children.push(h1('1. Resumen ejecutivo'));
children.push(plain('La integración actual con el broker de PASESA para autenticar a un asegurado por código 2FA expone tres endpoints distintos que el orquestador encadena en cada turno: INSURED_INITIATE genera el código, SEND_2FA_CODE_EMAIL lo envía por correo e INSURED_VALIDATE valida la respuesta del cliente. Mantener esta superficie obliga al equipo de PASESA a versionar tres contratos, al equipo de TalkIA a propagar el idCodigo entre eslabones vía lastActionResult overrides, y al agente IA a navegar una máquina de estados con dos saltos deterministas previos a recibir un dato útil para conversar.'));
children.push(plain('Esta propuesta consolida el flujo en dos endpoints (REQUEST_CODE y VALIDATE_CODE). El envío de correo deja de ser una acción visible: pasa a ser un detalle interno del broker. Resultado: una llamada HTTP menos por turno (≈40% menos latencia en el happy-path), un contrato menos por tenant, eliminación de la regla de auto-chaining y de los overrides de lastActionResult que hoy pasan idCodigo y correoDestino entre eslabones. La migración es retrocompatible: los endpoints viejos pueden seguir vivos durante el cutover.'));

children.push(calloutBox('Estado verificado en producción — 2 de junio de 2026', [
  plain('Confirmado directamente contra la base de datos de producción (no inferido de conversaciones previas):', { spacing: { after: 60 } }),
  bullet([t('Tenant PASESA activo: '), code('FCFBE7F3-99F1-4D56-B2C3-FF6D6239CCF3'), t('.')]),
  bullet([t('Las '), bold('3 acciones globales'), t(' del flujo 2FA están activas: '), code('INSURED_INITIATE'), t(', '), code('SEND_2FA_CODE_EMAIL'), t(' e '), code('INSURED_VALIDATE'), t('.')]),
  bullet([t('Las reglas de encadenamiento (ChainRules) viven hoy en el '), bold('DefaultWebhookContract'), t(' de '), code('INSURED_INITIATE'), t(' e '), code('INSURED_VALIDATE'), t('. '), code('SEND_2FA_CODE_EMAIL'), t(' es el eslabón terminal, sin encadenamiento propio.')]),
  bullet([t('Los slugs propuestos ('), code('INSURED_REQUEST_CODE'), t(' / '), code('INSURED_VALIDATE_CODE'), t(') '), bold('aún no existen'), t(' — esta es la línea base real sobre la que aplica la simplificación.')]),
]));

// 2. ESTADO ACTUAL
children.push(h1('2. Estado actual — Arquitectura de 3 endpoints'));
children.push(plain('Cada vez que un asegurado intenta consultar sus pólizas, el agente IA dispara la siguiente secuencia:'));

children.push(numbered([bold('INSURED_INITIATE'), t(' — El agente envía la cédula del asegurado. El broker genera un código de 6 dígitos y devuelve el correo enmascarado.')]));
children.push(numbered([bold('SEND_2FA_CODE_EMAIL'), t(' — Acción "puente" que el orquestador encadena automáticamente cuando status=CODIGO_GENERADO. Recibe el idCodigo y correoDestino del eslabón anterior y dispara el envío del email. Su respuesta es vacía.')]));
children.push(numbered([bold('INSURED_VALIDATE'), t(' — En el turno siguiente el agente envía el código que el cliente tipeó. El broker compara contra el idCodigo y, si es correcto, devuelve las pólizas.')]));

children.push(h2('2.1. Diagrama del flujo actual'));
children.push(...codeBlock([
  '  Cliente: "Mi cédula es 8-123-456"',
  '     │',
  '     ▼',
  '  ┌─────────────────────────────────────────────┐',
  '  │ TURNO 1                                     │',
  '  │  • LLM → [ACTION:INSURED_INITIATE]          │',
  '  │  • POST /broker/insured/initiate            │',
  '  │  ◄─ status=CODIGO_GENERADO, idCodigo, mail  │',
  '  │  • ChainRule matchea ⇒ encadena            │',
  '  │  • POST /broker/email/send-2fa              │',
  '  │  ◄─ 200 OK (vacío)                          │',
  '  │  • LLM (2da invocación) redacta respuesta   │',
  '  │  → "Te envié código a a***@pasesa.com"      │',
  '  └─────────────────────────────────────────────┘',
  '     │',
  '     ▼',
  '  Cliente: "423918"',
  '     │',
  '     ▼',
  '  ┌─────────────────────────────────────────────┐',
  '  │ TURNO 2                                     │',
  '  │  • LLM → [ACTION:INSURED_VALIDATE]          │',
  '  │  • PayloadBuilder resuelve idCodigo desde   │',
  '  │    lastActionResult.data del turno previo   │',
  '  │  • POST /broker/insured/validate            │',
  '  │  ◄─ status=OK, pólizas[]                    │',
  '  │  • LLM redacta respuesta con pólizas        │',
  '  └─────────────────────────────────────────────┘',
]));

children.push(h2('2.2. Estado del lado del broker'));
children.push(plain('Hoy el broker debe persistir un registro de códigos generados con tabla intermedia:'));
children.push(bullet([code('codigos_2fa(id_codigo, cedula, telefono, codigo_hash, expira_at, validado)')]));
children.push(plain('Cada turno consulta o muta esa tabla, y el idCodigo viaja en el wire desde el broker hasta AgentFlow y de vuelta. Esto agrega superficie de error: si AgentFlow pierde el idCodigo (cache flush, restart), el cliente debe reiniciar el flujo aunque el broker todavía tenga el código vivo.'));

// 3. PROBLEMAS IDENTIFICADOS
children.push(h1('3. Problemas identificados'));

const problemsTable = new Table({
  width: { size: 9360, type: WidthType.DXA },
  columnWidths: [2200, 4400, 2760],
  rows: [
    new TableRow({
      tableHeader: true,
      children: [
        headerCell('Problema', 2200),
        headerCell('Impacto operativo', 4400),
        headerCell('Severidad', 2760),
      ],
    }),
    new TableRow({ children: [
      cell(p([bold('Doble round-trip en el happy-path')]), { width: 2200 }),
      cell(plain('Cada identificación exitosa cuesta 2 llamadas al broker antes de poder responder al cliente. Si el broker tarda 600ms por endpoint, son 1.2s "ciegos" para el asegurado.'), { width: 4400 }),
      cell(p([t('Alta', { bold: true, color: RED })]), { width: 2760 }),
    ]}),
    new TableRow({ children: [
      cell(p([bold('Acción "puente" sin valor')]), { width: 2200 }),
      cell(plain('SEND_2FA_CODE_EMAIL no devuelve datos útiles. Existe únicamente porque hoy es una acción separable del broker. Consume un slot del catálogo del agente y debe documentarse en el Webhook Builder igual que cualquier otra.'), { width: 4400 }),
      cell(p([t('Media', { bold: true, color: AMBER })]), { width: 2760 }),
    ]}),
    new TableRow({ children: [
      cell(p([bold('Plumbing de idCodigo en lastActionResult')]), { width: 2200 }),
      cell(plain('El idCodigo nace en INSURED_INITIATE, lo necesitan SEND_2FA_CODE_EMAIL (puente) e INSURED_VALIDATE (turno siguiente). Hoy se resuelve con sourceType=lastActionResult.sourceKey y reglas especiales de persistencia ("guardar el primer eslabón, no el último"). Es código frágil y poco evidente.'), { width: 4400 }),
      cell(p([t('Alta', { bold: true, color: RED })]), { width: 2760 }),
    ]}),
    new TableRow({ children: [
      cell(p([bold('ChainRule obligatoria por tenant')]), { width: 2200 }),
      cell(plain('Cada corredor que use el flujo 2FA debe configurar la regla "status=CODIGO_GENERADO ⇒ ejecutar SEND_2FA_CODE_EMAIL". Hoy vive en el DefaultWebhookContract de INSURED_INITIATE; olvidarla deja al cliente sin email y sin mensaje de error visible.'), { width: 4400 }),
      cell(p([t('Media', { bold: true, color: AMBER })]), { width: 2760 }),
    ]}),
    new TableRow({ children: [
      cell(p([bold('Contratos paralelos a versionar')]), { width: 2200 }),
      cell(plain('Tres contratos JSON (request + response) mantenidos en sincronía entre el equipo de PASESA y el de TalkIA. Un cambio en el campo correoDestino impacta dos acciones.'), { width: 4400 }),
      cell(p([t('Media', { bold: true, color: AMBER })]), { width: 2760 }),
    ]}),
    new TableRow({ children: [
      cell(p([bold('Latencia compuesta visible al cliente')]), { width: 2200 }),
      cell(plain('El cliente percibe el "pensando..." durante: broker initiate + broker send-email + LLM (2 invocaciones por RegenerateReply). En redes celulares lentas son 4-5 segundos antes del primer mensaje útil.'), { width: 4400 }),
      cell(p([t('Alta', { bold: true, color: RED })]), { width: 2760 }),
    ]}),
  ],
});
children.push(problemsTable);

// 4. PROPUESTA
children.push(h1('4. Propuesta — Arquitectura de 2 endpoints'));
children.push(plain('Consolidar la generación + envío del código en un único endpoint del broker, y mantener un segundo endpoint para validar. El envío del email se vuelve una responsabilidad opaca del broker: AgentFlow no lo orquesta ni lo conoce.'));

children.push(h2('4.1. Diagrama del flujo propuesto'));
children.push(...codeBlock([
  '  Cliente: "Mi cédula es 8-123-456"',
  '     │',
  '     ▼',
  '  ┌─────────────────────────────────────────────┐',
  '  │ TURNO 1                                     │',
  '  │  • LLM → [ACTION:INSURED_REQUEST_CODE]      │',
  '  │  • POST /broker/insured/request-code        │',
  '  │     (cedula + telefonoOrigen)               │',
  '  │  ◄─ status, correoEnmascarado, nombre       │',
  '  │     (el broker ya envió el email)           │',
  '  │  • LLM redacta respuesta                    │',
  '  │  → "Te envié código a a***@pasesa.com"      │',
  '  └─────────────────────────────────────────────┘',
  '     │',
  '     ▼',
  '  Cliente: "423918"',
  '     │',
  '     ▼',
  '  ┌─────────────────────────────────────────────┐',
  '  │ TURNO 2                                     │',
  '  │  • LLM → [ACTION:INSURED_VALIDATE_CODE]     │',
  '  │  • POST /broker/insured/validate-code       │',
  '  │     (cedula + telefono + codigo)            │',
  '  │  ◄─ status=OK, pólizas[]                    │',
  '  │  • LLM redacta respuesta con pólizas        │',
  '  └─────────────────────────────────────────────┘',
]));

children.push(h2('4.2. Decisión clave: identificar la sesión por (cédula + teléfono)'));
children.push(plain('En el flujo propuesto AgentFlow ya no recibe ni propaga un idCodigo. La sesión 2FA se identifica server-side por la tupla (cédula, telefonoOrigen) que el broker ya recibe en ambos endpoints. El broker es libre de implementar el almacenamiento como prefiera (tabla, Redis, cache en memoria), y la rotación de códigos queda invisible al cliente y a AgentFlow.'));
children.push(plain('Beneficios concretos de eliminar idCodigo del wire:'));
children.push(bullet([t('Sin overrides en lastActionResult — el PayloadBuilder no necesita reglas especiales.')]));
children.push(bullet([t('Resiliente a flush de cache de AgentFlow: si el cliente envía el código y la conversación se reinició, el broker aún lo reconoce.')]));
children.push(bullet([t('Permite reenvíos: si el cliente dice "no me llegó", el agente reinvoca REQUEST_CODE y el broker decide si reutiliza el código vigente o genera uno nuevo (política del broker, no de TalkIA).')]));

// 5. COMPARATIVA
children.push(h1('5. Comparativa antes/después'));

const compareTable = new Table({
  width: { size: 9360, type: WidthType.DXA },
  columnWidths: [3120, 3120, 3120],
  rows: [
    new TableRow({
      tableHeader: true,
      children: [
        headerCell('Métrica', 3120),
        headerCell('Hoy (3 endpoints)', 3120),
        headerCell('Propuesto (2 endpoints)', 3120),
      ],
    }),
    new TableRow({ children: [
      cell(plain('Endpoints expuestos por PASESA'), { width: 3120 }),
      cell(plain('3'), { width: 3120 }),
      cell(p([t('2', { bold: true, color: GREEN })]), { width: 3120 }),
    ]}),
    new TableRow({ children: [
      cell(plain('Round-trips HTTP en turno 1'), { width: 3120 }),
      cell(plain('2 (initiate + send-email)'), { width: 3120 }),
      cell(p([t('1 (request-code)', { bold: true, color: GREEN })]), { width: 3120 }),
    ]}),
    new TableRow({ children: [
      cell(plain('Invocaciones al LLM en turno 1'), { width: 3120 }),
      cell(plain('2 (initial + regenerate-reply post-chain)'), { width: 3120 }),
      cell(p([t('1', { bold: true, color: GREEN })]), { width: 3120 }),
    ]}),
    new TableRow({ children: [
      cell(plain('ActionDefinitions a mantener'), { width: 3120 }),
      cell(plain('3 globales + asignar a cada tenant')),
      cell(p([t('2 globales', { bold: true, color: GREEN })]), { width: 3120 }),
    ]}),
    new TableRow({ children: [
      cell(plain('ChainRules por tenant'), { width: 3120 }),
      cell(plain('1 (status=CODIGO_GENERADO ⇒ SEND_2FA_CODE_EMAIL)')),
      cell(p([t('0', { bold: true, color: GREEN })]), { width: 3120 }),
    ]}),
    new TableRow({ children: [
      cell(plain('Campos pasados vía lastActionResult'), { width: 3120 }),
      cell(plain('idCodigo, correoDestino')),
      cell(p([t('— ninguno —', { bold: true, color: GREEN })]), { width: 3120 }),
    ]}),
    new TableRow({ children: [
      cell(plain('Latencia estimada turno 1')),
      cell(plain('≈ 3.0 s (2× broker + 2× LLM)')),
      cell(p([t('≈ 1.8 s', { bold: true, color: GREEN }), t(' (-40%)', { color: GREEN })])),
    ]}),
    new TableRow({ children: [
      cell(plain('Tokens LLM por turno 1')),
      cell(plain('≈ 2× (2 invocaciones)')),
      cell(p([t('1×', { bold: true, color: GREEN }), t(' (-50%)', { color: GREEN })])),
    ]}),
    new TableRow({ children: [
      cell(plain('Configuración por tenant nuevo')),
      cell(plain('3 contracts + ChainRule + asignaciones')),
      cell(p([t('2 contracts + asignaciones', { bold: true, color: GREEN })])),
    ]}),
  ],
});
children.push(compareTable);

// 6. CONTRATOS PROPUESTOS
children.push(h1('6. Contratos API propuestos'));

children.push(h2('6.1. POST /broker/insured/request-code'));
children.push(plain('Reemplaza INSURED_INITIATE + SEND_2FA_CODE_EMAIL.'));
children.push(plain('Request:'));
children.push(...codeBlock([
  '{',
  '  "cedula": "8-123-456",',
  '  "telefonoOrigen": "+50761234567"',
  '}',
]));
children.push(plain('Response 200 — código generado y enviado:'));
children.push(...codeBlock([
  '{',
  '  "status": "CODIGO_ENVIADO",',
  '  "correoEnmascarado": "a***@pasesa.com",',
  '  "nombreAsegurado": "JUAN PEREZ",',
  '  "expiraEnSegundos": 300',
  '}',
]));
children.push(plain('Response 200 — asegurado ya validado en visita anterior (auto-validación por teléfono):'));
children.push(...codeBlock([
  '{',
  '  "status": "AUTO_VALIDADO",',
  '  "nombreAsegurado": "JUAN PEREZ",',
  '  "polizas": [ ... ]',
  '}',
]));
children.push(plain('Response 404 — cédula no encontrada:'));
children.push(...codeBlock([
  '{',
  '  "status": "NO_ENCONTRADO",',
  '  "mensajeUsuario": "No encontré ese número de cédula. ¿Lo verificás?"',
  '}',
]));

children.push(h2('6.2. POST /broker/insured/validate-code'));
children.push(plain('Reemplaza INSURED_VALIDATE. Ya no recibe idCodigo: la sesión se resuelve por (cédula + teléfono).'));
children.push(plain('Request:'));
children.push(...codeBlock([
  '{',
  '  "cedula": "8-123-456",',
  '  "telefonoOrigen": "+50761234567",',
  '  "codigo": "423918"',
  '}',
]));
children.push(plain('Response 200 — código correcto:'));
children.push(...codeBlock([
  '{',
  '  "status": "VALIDADO",',
  '  "nombreAsegurado": "JUAN PEREZ",',
  '  "polizas": [',
  '    { "tipo": "AUTO", "numero": "P-001", "vigenciaHasta": "2026-12-31" },',
  '    { "tipo": "VIDA", "numero": "P-002", "vigenciaHasta": "2027-03-15" }',
  '  ]',
  '}',
]));
children.push(plain('Response 200 — código incorrecto/expirado (no se devuelve HTTP 4xx para que el LLM pueda redactar el reintento):'));
children.push(...codeBlock([
  '{',
  '  "status": "CODIGO_INVALIDO",',
  '  "intentosRestantes": 2,',
  '  "mensajeUsuario": "El código no coincide. Te quedan 2 intentos."',
  '}',
  '{',
  '  "status": "CODIGO_EXPIRADO",',
  '  "mensajeUsuario": "El código venció. Te genero uno nuevo."',
  '}',
]));

// 7. PLAN DE MIGRACION
children.push(h1('7. Plan de migración (sin downtime)'));
children.push(plain('Los endpoints viejos pueden coexistir con los nuevos durante la transición. El cutover es por tenant.'));

children.push(numbered([bold('Fase 1 — Implementación broker (PASESA): '), t('exponer los dos endpoints nuevos. Los viejos quedan vivos sin cambios.')]));
children.push(numbered([bold('Fase 2 — ActionDefinitions globales: '), t('crear INSURED_REQUEST_CODE e INSURED_VALIDATE_CODE como acciones globales en AgentFlow, sin asignarlas todavía.')]));
children.push(numbered([bold('Fase 3 — Pruebas en sandbox: '), t('asignar al tenant "Prueba" y validar con conversaciones reales que el flujo de 2 turnos funciona end-to-end.')]));
children.push(numbered([bold('Fase 4 — Cutover PASESA: '), t('en una ventana acordada, en el template de campaña de PASESA: (a) reemplazar las 3 acciones por las 2 nuevas, (b) actualizar el SystemPrompt para que el agente conozca los nuevos slugs, (c) eliminar la ChainRule del DefaultWebhookContract. El cambio es instantáneo — el siguiente turno usa el flujo nuevo.')]));
children.push(numbered([bold('Fase 5 — Decomisado: '), t('tras 30 días sin uso de los endpoints viejos (verificable en WebhookDispatchLogs), PASESA puede retirar el código legacy del broker.')]));

// 8. BENEFICIOS CUANTIFICADOS
children.push(h1('8. Beneficios cuantificados'));

children.push(bullet([bold('-40% latencia '), t('en el primer turno (1 round-trip al broker en vez de 2, 1 invocación al LLM en vez de 2). Concretamente, ~1.2 segundos menos de espera para el cliente.')]));
children.push(bullet([bold('-50% tokens del LLM '), t('por turno 1 (eliminar la regeneración post-chain). En volumen de PASESA, miles de tokens menos por día.')]));
children.push(bullet([bold('-33% endpoints '), t('expuestos por el broker. Una superficie de mantenimiento menor para PASESA.')]));
children.push(bullet([bold('-100% ChainRules '), t('por tenant para 2FA. Onboarding de nuevos corredores con el mismo broker se vuelve más simple.')]));
children.push(bullet([bold('Eliminación del bug latente '), t('de "idCodigo se pierde entre turnos" — el broker controla todo el estado.')]));
children.push(bullet([bold('Simplificación del prompt del agente: '), t('el SystemPrompt ya no necesita explicar cuándo invocar SEND_2FA_CODE_EMAIL (porque no existe). El catálogo de acciones del agente baja de 3 a 2 slots para el flujo de identificación.')]));

// 9. RIESGOS Y MITIGACIONES
children.push(h1('9. Riesgos y mitigaciones'));

const risksTable = new Table({
  width: { size: 9360, type: WidthType.DXA },
  columnWidths: [3120, 6240],
  rows: [
    new TableRow({
      tableHeader: true,
      children: [
        headerCell('Riesgo', 3120),
        headerCell('Mitigación', 6240),
      ],
    }),
    new TableRow({ children: [
      cell(plain('El broker no puede separar log de envío de email del log de generación de código.'), { width: 3120 }),
      cell(plain('No es bloqueante — internamente el broker puede seguir teniendo dos servicios. Solo se ocultan al cliente externo (AgentFlow).'), { width: 6240 }),
    ]}),
    new TableRow({ children: [
      cell(plain('Si el envío de email falla, el endpoint nuevo debe seguir devolviendo CODIGO_ENVIADO o falla todo el flujo.'), { width: 3120 }),
      cell(plain('Diseño: si el código se generó pero el email falla, el broker devuelve status=CODIGO_ENVIADO_PARCIAL con detalle. El LLM redacta un mensaje alternativo ("intentá de nuevo en 30 segundos").'), { width: 6240 }),
    ]}),
    new TableRow({ children: [
      cell(plain('Cambio de contrato rompe a tenants que ya tienen las 3 acciones asignadas.'), { width: 3120 }),
      cell(plain('Los slugs nuevos son distintos (REQUEST_CODE / VALIDATE_CODE). Las acciones viejas siguen funcionando hasta que el template del tenant se actualice. Cutover por tenant, no big-bang.'), { width: 6240 }),
    ]}),
    new TableRow({ children: [
      cell(plain('El broker debe ahora mantener mapeo (cédula+teléfono) → código en lugar de devolver idCodigo opaco.'), { width: 3120 }),
      cell(plain('Esto ya existe implícitamente — el broker debe poder reconocer el código en validate aunque el cliente reinicie la conversación. La nueva forma lo hace explícito.'), { width: 6240 }),
    ]}),
    new TableRow({ children: [
      cell(plain('Pruebas en sandbox podrían no cubrir todos los edge cases del broker (códigos expirados, reintentos).'), { width: 3120 }),
      cell(plain('Pasar checklist explícito en Fase 3: código correcto, código erróneo, código expirado, cédula inexistente, reenvío en menos de 30s, auto-validación por teléfono ya conocido.'), { width: 6240 }),
    ]}),
  ],
});
children.push(risksTable);

// 10. CONCLUSION
children.push(h1('10. Conclusión y próximos pasos'));
children.push(plain('La consolidación de los 3 endpoints actuales en 2 endpoints es retrocompatible, reduce latencia y costos de LLM, y elimina puntos de fragilidad documentados (overrides de lastActionResult, ChainRules obligatorias, acciones puente con response vacío). El esfuerzo de implementación está mayoritariamente en el lado del broker; del lado de AgentFlow solo requiere crear dos ActionDefinitions nuevas, asignarlas y actualizar el template de PASESA — el orquestador no necesita cambios.'));
children.push(plain('Próximos pasos sugeridos:'));
children.push(bullet([t('Validar el contrato propuesto con el equipo técnico de PASESA.')]));
children.push(bullet([t('Acordar quién implementa el endpoint /request-code (con envío de email interno) y la política de expiración/reintento del código.')]));
children.push(bullet([t('Confirmar la ventana de cutover y el plan de monitoreo en WebhookDispatchLogs durante las primeras 48 horas.')]));
children.push(bullet([t('Definir si el cambio aplica también a futuros corredores que usen el mismo broker, para que el rollout sea estándar.')]));

children.push(new Paragraph({
  spacing: { before: 600 },
  border: { top: { style: BorderStyle.SINGLE, size: 6, color: ACCENT, space: 8 } },
  children: [t(' ')],
}));
children.push(new Paragraph({
  alignment: AlignmentType.CENTER,
  children: [t('Documento técnico — AgentFlow / TalkIA — Junio 2026', { italics: true, color: '64748B', size: 18 })],
}));

// ---- documento ----
const doc = new Document({
  creator: 'AgentFlow',
  title: 'Propuesta de Simplificación — Endpoints 2FA PASESA (rev. 2)',
  description: 'Consolidación de 3 endpoints en 2 para el flujo 2FA del broker PASESA — Junio 2026',
  styles: {
    default: { document: { run: { font: 'Arial', size: 22 } } }, // 11pt body
    paragraphStyles: [
      { id: 'Heading1', name: 'Heading 1', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { size: 32, bold: true, font: 'Arial', color: NAVY },
        paragraph: { spacing: { before: 360, after: 180 }, outlineLevel: 0 } },
      { id: 'Heading2', name: 'Heading 2', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { size: 26, bold: true, font: 'Arial', color: ACCENT },
        paragraph: { spacing: { before: 240, after: 120 }, outlineLevel: 1 } },
      { id: 'Heading3', name: 'Heading 3', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { size: 24, bold: true, font: 'Arial', color: '0F172A' },
        paragraph: { spacing: { before: 180, after: 80 }, outlineLevel: 2 } },
    ],
  },
  numbering: {
    config: [
      { reference: 'bullets',
        levels: [{ level: 0, format: LevelFormat.BULLET, text: '•', alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 720, hanging: 360 } } } }] },
      { reference: 'numbers',
        levels: [{ level: 0, format: LevelFormat.DECIMAL, text: '%1.', alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 720, hanging: 360 } } } }] },
    ],
  },
  sections: [{
    properties: {
      page: {
        size: { width: 12240, height: 15840 }, // US Letter
        margin: { top: 1440, right: 1440, bottom: 1440, left: 1440 },
      },
    },
    headers: {
      default: new Header({
        children: [new Paragraph({
          alignment: AlignmentType.RIGHT,
          children: [t('Propuesta 2FA — PASESA', { italics: true, size: 18, color: '64748B' })],
        })],
      }),
    },
    footers: {
      default: new Footer({
        children: [new Paragraph({
          alignment: AlignmentType.CENTER,
          children: [
            t('AgentFlow / TalkIA  •  Página ', { size: 18, color: '64748B' }),
            new TextRun({ size: 18, color: '64748B', children: [PageNumber.CURRENT] }),
          ],
        })],
      }),
    },
    children,
  }],
});

const outPath = path.join(__dirname, 'Propuesta-Simplificacion-2FA-PASESA-Jun2026.docx');
Packer.toBuffer(doc).then(buf => {
  fs.writeFileSync(outPath, buf);
  console.log('OK ->', outPath);
}).catch(err => {
  console.error('ERROR:', err);
  process.exit(1);
});
