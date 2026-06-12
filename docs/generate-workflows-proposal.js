// Genera el documento de diseño "AgentFlow — Propuesta de Workflows".
// docx-js. Reusable: re-correr para regenerar si cambian datos.
const fs = require('fs')
const path = require('path')
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell,
  AlignmentType, LevelFormat, HeadingLevel, BorderStyle, WidthType, ShadingType,
  TableOfContents, PageBreak, Header, Footer, PageNumber,
} = require('docx')

const CONTENT_W = 9360 // US Letter, márgenes 1"
const NAVY = '1A3A6B'
const PURPLE = '6D28D9'
const GRAY = '6B7280'

// ---- helpers ----
const border = { style: BorderStyle.SINGLE, size: 1, color: 'CCCCCC' }
const borders = { top: border, bottom: border, left: border, right: border }
const cellMargins = { top: 80, bottom: 80, left: 120, right: 120 }

function h1(text) {
  return new Paragraph({ heading: HeadingLevel.HEADING_1, children: [new TextRun(text)] })
}
function h2(text) {
  return new Paragraph({ heading: HeadingLevel.HEADING_2, children: [new TextRun(text)] })
}
function p(text, opts = {}) {
  return new Paragraph({
    spacing: { after: 120, line: 276 },
    children: [new TextRun({ text, ...opts })],
  })
}
function rich(runs, opts = {}) {
  return new Paragraph({ spacing: { after: 120, line: 276 }, children: runs, ...opts })
}
function bullet(text, level = 0) {
  return new Paragraph({
    numbering: { reference: 'bullets', level },
    spacing: { after: 60 },
    children: typeof text === 'string' ? [new TextRun(text)] : text,
  })
}
function num(text) {
  return new Paragraph({
    numbering: { reference: 'numbers', level: 0 },
    spacing: { after: 80 },
    children: typeof text === 'string' ? [new TextRun(text)] : text,
  })
}
function headerCell(text, w) {
  return new TableCell({
    borders, width: { size: w, type: WidthType.DXA }, margins: cellMargins,
    shading: { fill: NAVY, type: ShadingType.CLEAR },
    children: [new Paragraph({ children: [new TextRun({ text, bold: true, color: 'FFFFFF', size: 19 })] })],
  })
}
function cell(content, w, opts = {}) {
  const runs = Array.isArray(content) ? content : [new TextRun({ text: String(content), size: 19 })]
  return new TableCell({
    borders, width: { size: w, type: WidthType.DXA }, margins: cellMargins,
    shading: opts.fill ? { fill: opts.fill, type: ShadingType.CLEAR } : undefined,
    children: [new Paragraph({ children: runs })],
  })
}
function table(widths, headerLabels, rows) {
  return new Table({
    width: { size: CONTENT_W, type: WidthType.DXA },
    columnWidths: widths,
    rows: [
      new TableRow({ tableHeader: true, children: headerLabels.map((l, i) => headerCell(l, widths[i])) }),
      ...rows.map((r) => new TableRow({
        children: r.map((c, i) => (c && c.__cell ? c.build(widths[i]) : cell(c, widths[i]))),
      })),
    ],
  })
}
// celda con shading opcional
function sc(content, fill) { return { __cell: true, build: (w) => cell(content, w, { fill }) } }

const doc = new Document({
  creator: 'AgentFlow',
  title: 'AgentFlow — Propuesta de Workflows',
  styles: {
    default: { document: { run: { font: 'Arial', size: 22 } } },
    paragraphStyles: [
      { id: 'Heading1', name: 'Heading 1', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { size: 30, bold: true, font: 'Arial', color: NAVY },
        paragraph: { spacing: { before: 280, after: 140 }, outlineLevel: 0 } },
      { id: 'Heading2', name: 'Heading 2', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { size: 25, bold: true, font: 'Arial', color: '234A85' },
        paragraph: { spacing: { before: 200, after: 100 }, outlineLevel: 1 } },
    ],
  },
  numbering: {
    config: [
      { reference: 'bullets', levels: [
        { level: 0, format: LevelFormat.BULLET, text: '•', alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 460, hanging: 260 } } } },
        { level: 1, format: LevelFormat.BULLET, text: '◦', alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 920, hanging: 260 } } } },
      ] },
      { reference: 'numbers', levels: [
        { level: 0, format: LevelFormat.DECIMAL, text: '%1.', alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 460, hanging: 260 } } } },
      ] },
    ],
  },
  sections: [{
    properties: {
      page: {
        size: { width: 12240, height: 15840 },
        margin: { top: 1440, right: 1440, bottom: 1440, left: 1440 },
      },
    },
    headers: {
      default: new Header({ children: [new Paragraph({
        alignment: AlignmentType.RIGHT,
        children: [new TextRun({ text: 'AgentFlow — Propuesta de Workflows', size: 16, color: GRAY })],
      })] }),
    },
    footers: {
      default: new Footer({ children: [new Paragraph({
        alignment: AlignmentType.CENTER,
        children: [new TextRun({ text: 'Página ', size: 16, color: GRAY }), new TextRun({ children: [PageNumber.CURRENT], size: 16, color: GRAY })],
      })] }),
    },
    children: [
      // ---- Portada ----
      new Paragraph({ spacing: { before: 2600, after: 0 }, alignment: AlignmentType.CENTER,
        children: [new TextRun({ text: 'AgentFlow', bold: true, size: 56, color: NAVY })] }),
      new Paragraph({ spacing: { before: 120, after: 0 }, alignment: AlignmentType.CENTER,
        children: [new TextRun({ text: 'Propuesta de Workflows', bold: true, size: 40, color: '234A85' })] }),
      new Paragraph({ spacing: { before: 80, after: 0 }, alignment: AlignmentType.CENTER,
        children: [new TextRun({ text: 'Orquestación visual de acciones para implementar lógica de negocio', size: 24, color: GRAY })] }),
      new Paragraph({ spacing: { before: 900, after: 0 }, alignment: AlignmentType.CENTER,
        children: [new TextRun({ text: 'Documento de diseño técnico', size: 22, italics: true, color: GRAY })] }),
      new Paragraph({ spacing: { before: 60 }, alignment: AlignmentType.CENTER,
        children: [new TextRun({ text: 'Junio 2026 · Borrador para revisión', size: 20, color: GRAY })] }),
      new Paragraph({ children: [new PageBreak()] }),

      // ---- TOC ----
      new Paragraph({ heading: HeadingLevel.HEADING_1, children: [new TextRun('Contenido')] }),
      new TableOfContents('Contenido', { hyperlink: true, headingStyleRange: '1-2' }),
      new Paragraph({ children: [new PageBreak()] }),

      // ---- 1. Resumen ejecutivo ----
      h1('1. Resumen ejecutivo'),
      p('AgentFlow ya permite que un agente IA ejecute acciones (webhooks, envío de email, descarga de datos, transferencia a humano) y, además, encadenar acciones de forma determinística mediante reglas configurables en la definición del webhook (ChainRules). Sin embargo, cuando un caso de negocio requiere anidar varias acciones en una secuencia — por ejemplo identificar al usuario, solicitar un PDF y descargar un estado de cuenta — esa lógica queda dispersa: una parte la decide el prompt del agente y otra vive escondida dentro del contrato de cada acción.'),
      p('Este documento propone introducir el concepto de Workflow: una capa de orquestación declarativa, con nombre, por encima de las acciones existentes. Un Workflow define la secuencia (o grafo) de pasos de un proceso de negocio de forma visible y editable por quien implementa la lógica, sin reemplazar al agente conversacional ni desmejorar lo que ya funciona. La propuesta es estrictamente aditiva e incremental.'),
      rich([
        new TextRun({ text: 'Tesis central: ', bold: true }),
        new TextRun('no convertir el agente en un diagrama de flujo rígido (como WhatChimp/ManyChat), sino darle al agente un “mapa” de workflows reutilizables que puede invocar, ejecutados por un orquestador determinístico que le devuelve el control en los puntos marcados.'),
      ]),

      // ---- 2. Estado actual ----
      h1('2. Estado actual: dos motores que conviven'),
      p('Hoy el sistema combina dos mecanismos para decidir qué acción se ejecuta y cuándo:'),
      table([2200, 3580, 3580],
        ['Motor', 'Cómo funciona', 'Fortaleza / Límite'],
        [
          ['A — Conducido por el LLM (ATP)', 'El agente emite [ACTION:slug] según el prompt y el contexto de la conversación. El backend parsea, valida contra el catálogo y ejecuta.', 'Flexible y conversacional. Pero no determinístico: el orden depende del juicio del modelo, por eso “el flujo no siempre es el mismo”.'],
          ['B — Determinístico (ChainRules)', 'En la definición del webhook (Paso 5 del builder) se declara: CUANDO la respuesta cumpla path==valor ENTONCES ejecutar la acción B, sin pasar por el LLM.', 'Determinístico y rápido. Pero por-acción y escondido: cada eslabón vive dentro del contrato de su acción.'],
        ]),
      p('Componentes reales involucrados hoy:', { bold: true }),
      bullet('Protocolo ATP (tags [ACTION] / [PARAM]) y el catálogo inyectado al prompt — ActionPromptBuilder.'),
      bullet('ChainRule (When → Then, con RegenerateReply) — resuelto por ActionChainResolver, con guardas: máximo 3 eslabones por turno, corte de ciclos, “primera regla que matchea gana”.'),
      bullet('Editor actual del encadenamiento: Paso 5 del Webhook Builder (Step5Chaining), operador único “equals”, path dot-notation simple.'),
      bullet('Paso de datos entre acciones: implícito, vía placeholders {campo} y LastActionResult persistido en Redis (consume-on-read en el siguiente turno).'),

      h2('2.1 Limitaciones que generan la fricción'),
      bullet('Visibilidad: el flujo completo de un proceso queda repartido en varios contratos de acción. Nadie lo ve de un vistazo.'),
      bullet('Expresividad: condición pobre (solo “equals”, sin arrays, sin rangos), secuencia lineal, sin ramas múltiples reales ni manejo de error configurable.'),
      bullet('Datos opacos: el implementador no ve qué variables consume y produce cada paso.'),
      bullet('Falta una primitiva de “pedir un dato al usuario” (cédula, adjuntar PDF); hoy eso lo improvisa el prompt.'),

      // ---- 3. El problema ----
      h1('3. El problema, en una frase'),
      rich([
        new TextRun({ text: 'Que quien implementa la lógica de un negocio pueda anidar y orquestar varias acciones en un flujo, de forma fluida y visible, ', italics: true }),
        new TextRun({ text: 'sin esconder reglas dentro de cada acción ni depender exclusivamente del prompt — y sin perder la flexibilidad conversacional que ya existe.', italics: true }),
      ]),

      // ---- 4. Por qué no copiar WhatChimp ----
      h1('4. Por qué copiar WhatChimp tal cual sería un retroceso'),
      p('La referencia visual (WhatChimp/ManyChat) es un flow-builder determinista puro: todo el recorrido es un árbol fijo de nodos (botones, listas, catálogo). No hay agente que entienda lenguaje natural; el usuario va “encarrilado”.'),
      p('AgentFlow ya tiene algo más potente: un agente conversacional. Reemplazarlo por un flowchart sería perder la capacidad de que el cliente se salga del guion y el agente igual responda. Además, esa referencia exhibe defectos a evitar:'),
      bullet('Cajas arrastradas a mano → con pocos pasos se vuelve un “plato de espagueti”.'),
      bullet('Conexiones difíciles de seguir visualmente.'),
      bullet('Mezcla estructura del flujo con métricas (Sent / Delivered / Errors) en el mismo nodo.'),
      bullet('Nodos sin tipado claro de su función.'),
      rich([
        new TextRun({ text: 'Conclusión: ', bold: true }),
        new TextRun('no cambiar el agente por un diagrama, sino darle al agente un mapa de workflows que puede invocar. Híbrido, no reemplazo.'),
      ]),

      // ---- 5. Propuesta ----
      h1('5. Propuesta: “Workflows” como capa de orquestación'),
      p('Un Workflow es una secuencia (o grafo) con nombre de pasos, por tenant. Cada paso (WorkflowStep) es de uno de estos tipos:'),
      table([2300, 7060],
        ['Tipo de paso', 'Qué hace'],
        [
          ['Acción', 'Ejecuta una Acción existente (webhook, email, descarga, transferir humano). Reusa todo lo actual.'],
          ['Recolectar', 'Pide un dato al usuario y espera su respuesta (cédula, adjuntar PDF). Pausa el workflow.'],
          ['Bifurcar', 'Ramifica según el resultado de un paso anterior (con más operadores que “equals”).'],
          ['Entregar al agente', 'Devuelve el control al LLM para que redacte la respuesta con los datos en mano (equivale al RegenerateReply actual).'],
        ]),
      rich([
        new TextRun({ text: 'El agente sigue siendo el director: ', bold: true }),
        new TextRun('en lugar de elegir acciones sueltas, puede invocar un workflow completo (p. ej. [WORKFLOW:identificacion_y_estado_cuenta]). El orquestador corre el grafo de forma determinística y le devuelve el control en los puntos marcados. Las ChainRules de hoy pasan a ser el caso más simple: un workflow lineal de dos pasos.'),
      ]),

      h2('5.1 Pieza clave: el contexto del workflow'),
      p('Un “bag” de variables que viaja por los pasos — lo que hoy hacen los placeholders y el LastActionResult de Redis, pero explícito y visible. Cada nodo declara qué consume ({cedula}) y qué produce ({saldo}). Esto elimina la magia invisible actual y habilita validación temprana (“usás {x} pero ningún paso lo produce”).'),

      // ---- 6. Modelo de datos ----
      h1('6. Modelo de datos (conceptual, aditivo)'),
      p('Se introduce de forma aditiva, sin migraciones disruptivas. Entidades nuevas, ninguna existente cambia de contrato.'),
      table([2300, 7060],
        ['Entidad', 'Campos principales'],
        [
          ['Workflow (por tenant)', 'Id, TenantId, Nombre, Slug, Descripción, Trigger (cómo se invoca: agente / evento / campaña), lista ordenada de WorkflowStep, Activo.'],
          ['WorkflowStep', 'Id, Tipo (Action | Collect | Branch | Handoff), ActionSlug (si aplica), InputMap (variables de contexto → inputs del paso), OutputMap (salida → contexto), Condición de avance, OnError (reintentar | saltar | abortar | mensaje), Siguiente(s) paso(s).'],
          ['Contexto de ejecución', 'Diccionario de variables que viaja por la ejecución del workflow para una conversación. Persistido por conversación cuando hay pasos “Recolectar”.'],
        ]),
      p('Nota de compatibilidad: un Workflow lineal puede “compilarse” a las mismas ChainRules que ya ejecuta el runtime actual. Eso permite entregar valor (la vista global con nombre) sin tocar el motor de ejecución en la primera fase.', { italics: true }),

      // ---- 7. UX ----
      h1('7. Experiencia de uso (lo que supera a la referencia)'),
      p('Dos vistas del mismo workflow, según preferencia del implementador:'),
      h2('7.1 Wizard por pasos'),
      p('La forma guiada que ya domina el equipo (igual que el Webhook Builder actual por pasos). Ideal para crear y editar sin pensar en diagramación.'),
      h2('7.2 Canvas visual'),
      p('Para ver el flujo completo de un vistazo — pero con mejoras concretas sobre WhatChimp:'),
      bullet('Auto-layout: el sistema acomoda los nodos; nada de arrastrar cajas a mano.'),
      bullet('Nodos tipados por color e ícono (Acción / Pregunta / Bifurcación / Entregar al agente).'),
      bullet('Clic en un nodo → panel lateral que reusa el Webhook Builder actual (no se edita en el canvas).'),
      bullet('Validación en vivo: marca en rojo un paso sin endpoint, una rama sin salida, o una variable {x} usada pero nunca producida.'),
      bullet('Estructura separada de métricas: las estadísticas no ensucian el nodo.'),
      bullet('Modo simulación: correr con datos mock y ver qué rama toma, sin enviar nada real.'),

      // ---- 8. Caso PASESA ----
      h1('8. Caso piloto: identificación + estado de cuenta'),
      p('Mapeo paso a paso de un proceso real (corredor PASESA/SOMOS) sobre el modelo propuesto. Es el candidato ideal para pilotear porque combina identificación, recolección de dato y consulta de datos:'),
      table([900, 2300, 3200, 2960],
        ['#', 'Tipo de paso', 'Qué hace', 'Contexto (consume → produce)'],
        [
          ['1', 'Acción', 'Identificar usuario (webhook al broker).', 'consume {cedula} → produce {status, polizaId, correoEnmascarado}'],
          ['2', 'Bifurcar', 'status = OK continúa; status = NO_ENCONTRADO va al paso de aclaración.', 'consume {status}'],
          ['3', 'Entregar al agente', 'Rama NO_ENCONTRADO: el agente pide confirmar la cédula y reinicia.', 'consume {status}'],
          ['4', 'Recolectar', 'Rama OK: pedir el documento/PDF requerido y esperar al cliente.', 'produce {pdf}'],
          ['5', 'Acción', 'Descargar estado de cuenta (webhook de datos).', 'consume {polizaId, pdf} → produce {saldo, vencimiento}'],
          ['6', 'Entregar al agente', 'Redactar respuesta final natural con los datos resueltos.', 'consume {saldo, vencimiento, correoEnmascarado}'],
        ]),
      p('Hoy este proceso se logra combinando prompt + varias ChainRules repartidas en los contratos de cada acción. Con Workflows, queda en un único objeto con nombre, versionable, simulable y visible para quien lo mantiene.', { italics: true }),

      // ---- 9. Camino incremental ----
      h1('9. Camino incremental (sin romper nada)'),
      table([1500, 3200, 4660],
        ['Fase', 'Entrega', 'Impacto en el runtime'],
        [
          [sc('Fase 0', 'EEF3FB'), sc('Hoy: ChainRules por-acción siguen funcionando igual.', 'EEF3FB'), sc('Ninguno.', 'EEF3FB')],
          ['Fase 1', 'Entidad Workflow que envuelve lo existente. Vista de lista/wizard. Un workflow lineal se compila a las ChainRules actuales.', 'Cero cambios en el motor de ejecución. Ganan la vista global con nombre.'],
          ['Fase 2', 'Pasos nuevos: Recolectar, Bifurcar con más operadores, manejo de error.', 'El runtime se extiende; las Acciones no cambian.'],
          ['Fase 3', 'Canvas visual + modo simulación.', 'Solo frontend + endpoint de simulación; el runtime ya existe.'],
        ]),
      rich([
        new TextRun({ text: 'En todo momento ', bold: true }),
        new TextRun('el agente puede seguir disparando acciones sueltas vía ATP. El Workflow es opt-in por caso de negocio.'),
      ]),

      // ---- 10. Decisiones a cerrar ----
      h1('10. Decisiones a cerrar antes de diseñar a fondo'),
      p('Definen el alcance y la complejidad (especialmente la #4, la más cara):'),
      num('Disparo: ¿el workflow lo invoca solo el agente (por intención), o también eventos (inicio de campaña, webhook externo, scheduled job)? ¿Ambos?'),
      num('Quién lo arma: ¿solo super admin (como el Webhook Builder hoy), o también el tenant para su propio negocio?'),
      num('Ramificación: ¿se necesitan ramas múltiples reales (árbol), o basta secuencia + salida temprana? Define si el canvas es simple o complejo.'),
      num('Paso “Recolectar”: ¿el workflow debe pausar y esperar la respuesta del cliente (máquina de estados por conversación)? Es lo más potente, pero implica persistir el estado del workflow por conversación.'),
      num('Condiciones: ¿qué operadores faltan ya en lo real (notEquals, contains, isNotNull, rangos numéricos)?'),

      // ---- 11. Recomendación ----
      h1('11. Recomendación'),
      p('Arrancar con Fase 1 + parte de Fase 2: entidad Workflow con vista de lista/wizard que envuelve las ChainRules y agrega el paso “Recolectar” y más operadores de condición. Dejar el canvas visual para una segunda iteración, una vez validado el modelo con un caso real.'),
      p('Pilotear con el proceso de identificación + estado de cuenta de PASESA/SOMOS, que es representativo y ya conocido. Si el modelo de datos y la UX resuelven ese caso de punta a punta de forma fluida, se generaliza al resto.'),
    ],
  }],
})

const outDir = path.join(__dirname)
const out = path.join(outDir, 'AgentFlow-Propuesta-Workflows.docx')
Packer.toBuffer(doc).then((buf) => {
  fs.writeFileSync(out, buf)
  console.log('OK ->', out)
})
