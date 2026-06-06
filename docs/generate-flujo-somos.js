// Genera el documento Word del flujo operativo de SOMOS Seguros.
// Uso: node generate-flujo-somos.js   (requiere NODE_PATH al docx global)
// Salida: ./Flujo-Operativo-SOMOS-Seguros.docx

const fs = require('fs');
const path = require('path');
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  AlignmentType, LevelFormat, HeadingLevel, BorderStyle, WidthType,
  ShadingType, PageNumber, Header, Footer,
} = require('docx');

const NAVY = '1A3A6B', ACCENT = '2E75B6', GREEN = '047857', AMBER = 'D97706',
      RED = 'B91C1C', GRAY_BG = 'F1F5F9', CALLOUT_BG = 'EAF2FB', GRAY_BORDER = 'CCCCCC';
const border = { style: BorderStyle.SINGLE, size: 1, color: GRAY_BORDER };
const borders = { top: border, bottom: border, left: border, right: border };

const t = (text, opts = {}) => new TextRun({ text, ...opts });
const p = (children, opts = {}) => new Paragraph({ children, ...opts });
const plain = (text, opts = {}) => p([t(text)], opts);
const bold = (text) => t(text, { bold: true });
const code = (text) => t(text, { font: 'Consolas', size: 18 });

const h1 = (text) => new Paragraph({ heading: HeadingLevel.HEADING_1, children: [t(text)], spacing: { before: 320, after: 160 } });
const h2 = (text) => new Paragraph({ heading: HeadingLevel.HEADING_2, children: [t(text)], spacing: { before: 240, after: 120 } });
const bullet = (children) => new Paragraph({ numbering: { reference: 'bullets', level: 0 }, children });
const numbered = (children) => new Paragraph({ numbering: { reference: 'numbers', level: 0 }, children });

const cell = (children, opts = {}) => new TableCell({
  borders,
  width: { size: opts.width ?? 3120, type: WidthType.DXA },
  shading: opts.shading ? { fill: opts.shading, type: ShadingType.CLEAR } : undefined,
  margins: { top: 90, bottom: 90, left: 130, right: 130 },
  children: Array.isArray(children) ? children : [children],
});
const headerCell = (text, width) => cell(p([t(text, { bold: true, color: 'FFFFFF' })]), { width, shading: NAVY });

const codeBlock = (lines) => lines.map(line => new Paragraph({
  children: [code(line.length ? line : ' ')],
  shading: { fill: GRAY_BG, type: ShadingType.CLEAR },
  spacing: { before: 0, after: 0 },
}));

const calloutBox = (titleText, paragraphs, accent = ACCENT) => new Table({
  width: { size: 9360, type: WidthType.DXA },
  columnWidths: [9360],
  borders: {
    top: { style: BorderStyle.SINGLE, size: 1, color: accent },
    bottom: { style: BorderStyle.SINGLE, size: 1, color: accent },
    left: { style: BorderStyle.SINGLE, size: 18, color: accent },
    right: { style: BorderStyle.SINGLE, size: 1, color: accent },
  },
  rows: [new TableRow({ children: [new TableCell({
    width: { size: 9360, type: WidthType.DXA },
    shading: { fill: CALLOUT_BG, type: ShadingType.CLEAR },
    margins: { top: 120, bottom: 120, left: 180, right: 160 },
    children: [p([t(titleText, { bold: true, color: NAVY, size: 20 })], { spacing: { after: 80 } }), ...paragraphs],
  })] })],
});

const children = [];

// ── PORTADA ──
children.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 2200, after: 200 },
  children: [t('Flujo Operativo — SOMOS Seguros', { bold: true, size: 44, color: NAVY })] }));
children.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 200 },
  children: [t('Estado actual, recomendación anti-baneo y flujo alternativo con plataforma propia (Meta)', { bold: true, size: 26, color: ACCENT })] }));
children.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 600 },
  border: { bottom: { style: BorderStyle.SINGLE, size: 12, color: ACCENT, space: 1 } }, children: [t(' ')] }));
children.push(new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 100 }, children: [t('AgentFlow / TalkIA', { size: 24, color: '475569' })] }));
children.push(new Paragraph({ alignment: AlignmentType.CENTER, children: [t('Documento operativo — Junio 2026', { size: 22, color: '64748B' })] }));
children.push(new Paragraph({ children: [t(' ')], pageBreakBefore: true }));

// ── 1. RESUMEN EJECUTIVO ──
children.push(h1('1. Resumen ejecutivo'));
children.push(plain('Este documento describe el flujo operativo actual de gestión de cobros de SOMOS Seguros en TalkIA — desde la descarga de la morosidad, la generación de campañas, el envío y el seguimiento automatizado — y plantea dos mejoras:'));
children.push(numbered([bold('Recomendación anti-baneo: '), t('distribuir el total de contactos de cada descarga entre los días hábiles de la semana (lunes a viernes), de modo que no se superen ~40 contactos nuevos por día. La línea de SOMOS opera sobre UltraMsg (no es API oficial de WhatsApp), por lo que el volumen diario es el principal factor de riesgo de bloqueo.')]));
children.push(numbered([bold('Flujo alternativo (Meta): '), t('si SOMOS opera su propia plataforma integrada con la API oficial de Meta (WhatsApp Cloud API) y es ella quien envía los mensajes, TalkIA puede encargarse únicamente de responder las dudas y respuestas de los clientes. Esto reduce drásticamente el riesgo de baneo, pero requiere un proceso adicional de integración entre la plataforma de SOMOS y TalkIA.')]));

// ── 2. FLUJO ACTUAL ──
children.push(h1('2. Flujo actual (UltraMsg, gestionado por TalkIA de extremo a extremo)'));
children.push(plain('Hoy TalkIA cubre todo el ciclo sobre la línea WhatsApp de SOMOS conectada por UltraMsg (número +507 6204-9182, instancia 133282). El ciclo tiene cinco etapas:'));

children.push(h2('2.1. Descarga de morosidad'));
children.push(bullet([t('El equipo de cobros descarga del sistema de la aseguradora el archivo de morosidad (clientes con saldos vencidos) y lo sube a TalkIA, o se descarga vía el módulo de morosidad integrado.')]));
children.push(bullet([t('El archivo trae, por cliente: nombre, identificación, teléfono, póliza, aseguradora, monto pendiente y — cuando aplica — el ejecutivo asignado.')]));

children.push(h2('2.2. Generación de campañas'));
children.push(bullet([bold('Validación de teléfonos: '), t('TalkIA normaliza a formato E.164 panameño y descarta inválidos (menos de 7 dígitos, solo ceros, el bug +507507, duplicados dentro del archivo y contra campañas activas). Esto evita disparar mensajes a números que provocan bloqueos.')]));
children.push(bullet([bold('Separación por ejecutivo: '), t('si el archivo trae el correo/teléfono del ejecutivo, las campañas se dividen por ejecutivo para que cada uno atienda a sus propios clientes en el Monitor.')]));
children.push(bullet([bold('Creación: '), t('se crea la Campaña + los Contactos de campaña en base de datos, listos para envío en el horario configurado.')]));

children.push(h2('2.3. Envío inicial (con control anti-baneo)'));
children.push(plain('El despachador envía el primer mensaje a cada contacto respetando límites de tasa por tenant y solo en horario laboral. Antes de cada lote verifica que la línea esté conectada (pre-chequeo de salud): si la línea está caída, difiere el envío en vez de encolar mensajes que se dispararían en ráfaga al reconectar.'));

children.push(h2('2.4. Seguimiento (follow-ups)'));
children.push(bullet([t('Si el cliente no responde, TalkIA envía recordatorios automáticos según los tiempos configurados en la plantilla (follow-up hours), solo en horario laboral y solo si la conversación sigue esperando al cliente.')]));
children.push(bullet([t('El seguimiento respeta los MISMOS límites de tasa que el envío inicial, y también verifica la salud de la línea antes de enviar (no manda follow-ups si la línea está caída).')]));

children.push(h2('2.5. Etiquetado y cierre'));
children.push(bullet([t('Cada conversación se etiqueta automáticamente (Confirmó Pago, Promesa de Pago, Negociación, Disputa, etc.) para medir efectividad.')]));
children.push(bullet([t('Las conversaciones inactivas se cierran automáticamente tras el tiempo configurado, y se envía un resumen de gestión.')]));

children.push(h2('2.6. Diagrama del flujo actual'));
children.push(...codeBlock([
  '  Aseguradora                 SOMOS / TalkIA                  Cliente',
  '  ───────────                 ──────────────                  ───────',
  '  Archivo morosidad  ──►  [1] Descarga / carga',
  '                          [2] Validación E.164 + split',
  '                          [3] Campaña + contactos',
  '                              │',
  '                              ▼ (horario 8-17h, límites de tasa,',
  '                                 pre-chequeo de línea)',
  '                          [4] Envío inicial  ───────────────►  WhatsApp',
  '                              │                                   │',
  '                              │   ◄──────── responde / no ────────┘',
  '                              ▼',
  '                          [5] Agente IA responde / Seguimiento',
  '                              Etiquetado + cierre + resumen',
]));

children.push(h2('2.7. Parámetros actuales de envío de SOMOS'));
children.push(new Table({
  width: { size: 9360, type: WidthType.DXA },
  columnWidths: [4680, 2340, 2340],
  rows: [
    new TableRow({ tableHeader: true, children: [headerCell('Parámetro', 4680), headerCell('Valor actual', 2340), headerCell('Efecto', 2340)] }),
    new TableRow({ children: [cell(plain('Mensajes por minuto'), { width: 4680 }), cell(p([t('2', { bold: true })]), { width: 2340 }), cell(plain('Ritmo de envío'), { width: 2340 })] }),
    new TableRow({ children: [cell(plain('Máximo por hora'), { width: 4680 }), cell(p([t('30', { bold: true })]), { width: 2340 }), cell(plain('Tope horario'), { width: 2340 })] }),
    new TableRow({ children: [cell(plain('Máximo por día'), { width: 4680 }), cell(p([t('100', { bold: true, color: AMBER })]), { width: 2340 }), cell(plain('Tope diario (alto)'), { width: 2340 })] }),
    new TableRow({ children: [cell(plain('Tamaño de lote / enfriamiento'), { width: 4680 }), cell(p([t('15 / 20 min', { bold: true })]), { width: 2340 }), cell(plain('Pausa entre lotes'), { width: 2340 })] }),
    new TableRow({ children: [cell(plain('Delay entre mensajes'), { width: 4680 }), cell(p([t('5 s', { bold: true })]), { width: 2340 }), cell(plain('Jitter anti-patrón'), { width: 2340 })] }),
    new TableRow({ children: [cell(plain('Horario / Zona'), { width: 4680 }), cell(p([t('08:00–17:00', { bold: true })]), { width: 2340 }), cell(plain('America/Panama'), { width: 2340 })] }),
  ],
}));
children.push(plain('Campañas recientes de SOMOS: entre 54 y 95 contactos por descarga. Con el tope diario actual de 100, una descarga completa puede salir casi toda en un solo día — justo el patrón de alto volumen que conviene evitar en UltraMsg.', { spacing: { before: 120 } }));

// ── 3. RECOMENDACIÓN ANTI-BANEO ──
children.push(new Paragraph({ children: [t(' ')], pageBreakBefore: true }));
children.push(h1('3. Recomendación anti-baneo: distribuir contactos (≤40/día, lunes a viernes)'));

children.push(calloutBox('Por qué', [
  plain('UltraMsg conecta un número de WhatsApp normal por escaneo de QR — NO es la API oficial de Meta. Meta detecta como spam los envíos masivos desde números no oficiales, sobre todo cuando hay alta tasa diaria y/o números inválidos. El resultado es la desvinculación/baneo de la línea (incidente ya vivido). El factor de riesgo dominante es el volumen diario de mensajes salientes nuevos.'),
], AMBER));

children.push(plain('Propuesta: en vez de enviar toda la descarga en uno o dos días, repartir el total de contactos entre los días hábiles de la semana, de forma que no se superen ~40 contactos nuevos por día (lunes a viernes). El seguimiento de los días previos convive con los nuevos envíos sin sumar al "nuevo" diario de forma agresiva.', { spacing: { before: 120 } }));

children.push(h2('3.1. Cómo se aplica'));
children.push(numbered([bold('Bajar el tope diario: '), t('configurar el "Máximo por día" de SOMOS de 100 a 40. Es el techo duro que impide pasarse aunque se cargue una lista grande.')]));
children.push(numbered([bold('Trocear la descarga: '), t('dividir la lista de la semana entre los 5 días hábiles. Si el total ÷ 5 ≤ 40, se reparte parejo; si es mayor, se topa en 40/día y el resto pasa al día siguiente.')]));
children.push(numbered([bold('Mantener horario y ritmo: '), t('seguir enviando solo 08:00–17:00, con los lotes y el enfriamiento actuales (patrón humano, no ráfaga).')]));

children.push(h2('3.2. Ejemplos de distribución'));
children.push(new Table({
  width: { size: 9360, type: WidthType.DXA },
  columnWidths: [2600, 6760],
  rows: [
    new TableRow({ tableHeader: true, children: [headerCell('Total de la descarga', 2600), headerCell('Distribución sugerida (L–V, ≤40/día)', 6760)] }),
    new TableRow({ children: [cell(p([t('95 contactos', { bold: true })]), { width: 2600 }), cell(p([t('≈ 19/día durante 5 días', { color: GREEN })]), { width: 6760 })] }),
    new TableRow({ children: [cell(p([t('150 contactos', { bold: true })]), { width: 2600 }), cell(p([t('30/día durante 5 días', { color: GREEN })]), { width: 6760 })] }),
    new TableRow({ children: [cell(p([t('200 contactos', { bold: true })]), { width: 2600 }), cell(p([t('40/día durante 5 días (al tope)', { color: GREEN })]), { width: 6760 })] }),
    new TableRow({ children: [cell(p([t('260 contactos', { bold: true })]), { width: 2600 }), cell(plain('40/día → se necesitan 7 días hábiles (desborda a la semana siguiente)'), { width: 6760 })] }),
  ],
}));

children.push(h2('3.3. Beneficios'));
children.push(bullet([bold('Menor riesgo de baneo: '), t('un ritmo diario bajo y constante se parece a actividad humana, no a un blast masivo.')]));
children.push(bullet([bold('Mejor gestión: '), t('40 conversaciones nuevas por día son manejables por el equipo; con 95 de golpe, muchas quedan sin atención humana oportuna.')]));
children.push(bullet([bold('Continuidad: '), t('si la línea se cae un día, solo se afecta ese bloque pequeño, no toda la campaña.')]));
children.push(bullet([bold('Sin desarrollo: '), t('se logra solo con configuración (tope diario = 40) + práctica operativa (trocear la carga). No requiere cambios de código.')]));

// ── 4. FLUJO ALTERNATIVO (META) ──
children.push(new Paragraph({ children: [t(' ')], pageBreakBefore: true }));
children.push(h1('4. Flujo alternativo: plataforma propia de SOMOS integrada con Meta'));
children.push(plain('En este modelo, SOMOS opera su propia plataforma conectada a la API oficial de Meta (WhatsApp Cloud API) y es ELLA quien envía los mensajes de cobro. TalkIA deja de enviar las campañas y se especializa en lo que mejor hace: responder de forma inteligente las dudas y respuestas de los clientes.'));

children.push(h2('4.1. Reparto de responsabilidades'));
children.push(new Table({
  width: { size: 9360, type: WidthType.DXA },
  columnWidths: [4680, 4680],
  rows: [
    new TableRow({ tableHeader: true, children: [headerCell('Plataforma de SOMOS (Meta oficial)', 4680), headerCell('TalkIA', 4680)] }),
    new TableRow({ children: [
      cell([plain('• Envía los mensajes de cobro salientes (plantillas aprobadas por Meta).'), plain('• Administra el número, la cuenta de WhatsApp Business (WABA) y las plantillas.')], { width: 4680 }),
      cell([plain('• Recibe las respuestas/dudas de los clientes.'), plain('• El agente IA interpreta y redacta la respuesta.'), plain('• Etiqueta y registra la gestión.')], { width: 4680 }),
    ]}),
  ],
}));

children.push(h2('4.2. Diagrama del flujo propuesto'));
children.push(...codeBlock([
  '  Plataforma SOMOS (Meta Cloud API)              TalkIA',
  '  ─────────────────────────────────             ──────',
  '  Envía mensaje de cobro  ───────►  Cliente',
  '                                       │',
  '                                       ▼ (el cliente responde / pregunta)',
  '  Webhook Meta ◄────────────────────  WhatsApp',
  '       │',
  '       │  (1) reenvía el entrante  ──────────────►  [API TalkIA]',
  '       │                                              │  agente IA',
  '       │  (2) respuesta del agente  ◄───────────────  redacta',
  '       ▼',
  '  Envía la respuesta al cliente vía Meta',
  '       (o TalkIA la envía con las credenciales Meta delegadas)',
]));
children.push(plain('Variante: TalkIA puede recibir el webhook de Meta directamente y responder usando las credenciales del número de SOMOS (TalkIA ya soporta Meta Cloud API como canal). La elección depende de quién debe "poseer" el envío de la respuesta.', { spacing: { before: 100 } }));

children.push(h2('4.3. Componentes de la integración (proceso adicional)'));
children.push(plain('Este modelo NO es solo configuración: requiere construir un puente de integración entre la plataforma de SOMOS y TalkIA. Los puntos a acordar e implementar:'));
children.push(bullet([bold('Entrada (inbound): '), t('cómo llega a TalkIA cada respuesta del cliente — webhook desde la plataforma de SOMOS hacia un endpoint de TalkIA, con un formato de payload acordado (teléfono, texto, identificadores).')]));
children.push(bullet([bold('Salida (outbound): '), t('cómo se envía la respuesta del agente — o TalkIA la devuelve a la plataforma de SOMOS para que ella la mande, o TalkIA la envía directo por Meta con credenciales delegadas.')]));
children.push(bullet([bold('Identidad de la conversación: '), t('correlacionar cada mensaje con el cliente correcto por teléfono + número de SOMOS, para no mezclar conversaciones.')]));
children.push(bullet([bold('Ventana de 24 horas de Meta: '), t('fuera de la ventana de servicio de 24h, Meta solo permite plantillas aprobadas; hay que definir qué se responde y cómo dentro y fuera de esa ventana.')]));
children.push(bullet([bold('Plantillas y WABA: '), t('SOMOS gestiona la aprobación de plantillas y la titularidad del número/WABA en Meta.')]));
children.push(bullet([bold('Autenticación y seguridad: '), t('tokens/credenciales del puente, validación de firma de los webhooks, y manejo de reintentos/duplicados.')]));

children.push(h2('4.4. Ventajas y consideraciones'));
children.push(bullet([bold('Ventaja principal: '), t('al usar la API oficial de Meta, el riesgo de baneo por volumen prácticamente desaparece y se habilitan volúmenes mucho mayores que UltraMsg.')]));
children.push(bullet([bold('SOMOS controla el envío: '), t('mantiene su operación de salida en su plataforma; TalkIA aporta solo la capa conversacional inteligente.')]));
children.push(bullet([bold('Consideración — esfuerzo: '), t('es un proyecto de integración (definición de contrato, desarrollo del puente, pruebas), no un cambio inmediato. Conviene un piloto controlado antes de migrar el volumen completo.')]));
children.push(bullet([bold('Consideración — costos: '), t('Meta cobra por conversación iniciada por la empresa; hay que dimensionar el costo vs. el ahorro de riesgo.')]));

// ── 5. COMPARATIVA ──
children.push(h1('5. Comparativa de los dos modelos'));
children.push(new Table({
  width: { size: 9360, type: WidthType.DXA },
  columnWidths: [3120, 3120, 3120],
  rows: [
    new TableRow({ tableHeader: true, children: [headerCell('Aspecto', 3120), headerCell('Actual (UltraMsg, TalkIA envía)', 3120), headerCell('Alternativo (Meta, SOMOS envía)', 3120)] }),
    new TableRow({ children: [cell(plain('Quién envía'), { width: 3120 }), cell(plain('TalkIA'), { width: 3120 }), cell(plain('Plataforma de SOMOS'), { width: 3120 })] }),
    new TableRow({ children: [cell(plain('Quién responde dudas'), { width: 3120 }), cell(plain('TalkIA (agente IA)'), { width: 3120 }), cell(plain('TalkIA (agente IA)'), { width: 3120 })] }),
    new TableRow({ children: [cell(plain('Canal'), { width: 3120 }), cell(plain('UltraMsg (no oficial)'), { width: 3120 }), cell(p([t('Meta Cloud API (oficial)', { color: GREEN })]), { width: 3120 })] }),
    new TableRow({ children: [cell(plain('Riesgo de baneo'), { width: 3120 }), cell(p([t('Medio/Alto en volumen', { color: AMBER })]), { width: 3120 }), cell(p([t('Bajo', { bold: true, color: GREEN })]), { width: 3120 })] }),
    new TableRow({ children: [cell(plain('Volumen diario seguro'), { width: 3120 }), cell(plain('≤ 40/día recomendado')), cell(plain('Alto (según plan Meta)'))] }),
    new TableRow({ children: [cell(plain('Esfuerzo'), { width: 3120 }), cell(p([t('Ya operativo', { color: GREEN })]), { width: 3120 }), cell(p([t('Integración adicional', { color: AMBER })]), { width: 3120 })] }),
  ],
}));

// ── 6. CONCLUSIÓN ──
children.push(h1('6. Conclusión y próximos pasos'));
children.push(plain('El flujo actual sobre UltraMsg funciona de extremo a extremo, pero su punto débil es el riesgo de baneo por volumen. La acción inmediata y sin desarrollo es distribuir cada descarga en ≤40 contactos por día (lunes a viernes) bajando el tope diario a 40. A mediano plazo, integrar la plataforma de SOMOS con Meta — enviando ellos y respondiendo TalkIA — elimina el riesgo de baneo y habilita mayor volumen, a cambio de un proyecto de integración.'));
children.push(plain('Próximos pasos sugeridos:', { spacing: { before: 100 } }));
children.push(bullet([t('Aplicar el tope de 40/día y acordar con el equipo la práctica de trocear la descarga semanal.')]));
children.push(bullet([t('Monitorear la salud de la línea y la tasa de respuesta durante 2–3 semanas con el nuevo ritmo.')]));
children.push(bullet([t('Si SOMOS decide avanzar con Meta, abrir una fase de diseño del contrato de integración (inbound/outbound, ventana 24h, plantillas) y un piloto controlado.')]));

children.push(new Paragraph({ spacing: { before: 500 },
  border: { top: { style: BorderStyle.SINGLE, size: 6, color: ACCENT, space: 8 } }, children: [t(' ')] }));
children.push(new Paragraph({ alignment: AlignmentType.CENTER,
  children: [t('Documento operativo — AgentFlow / TalkIA — SOMOS Seguros — Junio 2026', { italics: true, color: '64748B', size: 18 })] }));

// ── DOCUMENTO ──
const doc = new Document({
  creator: 'AgentFlow',
  title: 'Flujo Operativo — SOMOS Seguros',
  description: 'Flujo actual, recomendación anti-baneo y flujo alternativo con Meta',
  styles: {
    default: { document: { run: { font: 'Arial', size: 22 } } },
    paragraphStyles: [
      { id: 'Heading1', name: 'Heading 1', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { size: 30, bold: true, font: 'Arial', color: NAVY }, paragraph: { spacing: { before: 360, after: 180 }, outlineLevel: 0 } },
      { id: 'Heading2', name: 'Heading 2', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { size: 25, bold: true, font: 'Arial', color: ACCENT }, paragraph: { spacing: { before: 240, after: 120 }, outlineLevel: 1 } },
    ],
  },
  numbering: { config: [
    { reference: 'bullets', levels: [{ level: 0, format: LevelFormat.BULLET, text: '•', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 720, hanging: 360 } } } }] },
    { reference: 'numbers', levels: [{ level: 0, format: LevelFormat.DECIMAL, text: '%1.', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 720, hanging: 360 } } } }] },
  ] },
  sections: [{
    properties: { page: { size: { width: 12240, height: 15840 }, margin: { top: 1440, right: 1440, bottom: 1440, left: 1440 } } },
    headers: { default: new Header({ children: [new Paragraph({ alignment: AlignmentType.RIGHT, children: [t('Flujo Operativo — SOMOS Seguros', { italics: true, size: 18, color: '64748B' })] })] }) },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER, children: [t('AgentFlow / TalkIA  •  Página ', { size: 18, color: '64748B' }), new TextRun({ size: 18, color: '64748B', children: [PageNumber.CURRENT] })] })] }) },
    children,
  }],
});

const outPath = path.join(__dirname, 'Flujo-Operativo-SOMOS-Seguros.docx');
Packer.toBuffer(doc).then(buf => { fs.writeFileSync(outPath, buf); console.log('OK ->', outPath); })
  .catch(err => { console.error('ERROR:', err); process.exit(1); });
