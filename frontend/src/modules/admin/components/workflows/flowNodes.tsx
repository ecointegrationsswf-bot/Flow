import { memo } from 'react'
import { Handle, Position, type NodeProps } from 'reactflow'
import {
  Play, Square, Zap, GitBranch, Bot, Lock, Clock, MessageSquare,
  type LucideIcon,
} from 'lucide-react'

// Motor de flujos — Fase 4. Definición visual de los nodos del lienzo.
// Cada tipo de nodo del mockup: Acción (rectángulo), Condición (rombo), LLM, etc.

interface NodeDef {
  label: string
  icon: LucideIcon
  cls: string
  target: boolean
  source: boolean
}

const NODE_DEFS: Record<string, NodeDef> = {
  start:   { label: 'Inicio',  icon: Play,          cls: 'bg-emerald-600', target: false, source: true },
  action:  { label: 'Acción',  icon: Zap,           cls: 'bg-blue-600',    target: true,  source: true },
  llm:     { label: 'LLM',     icon: Bot,           cls: 'bg-indigo-600',  target: true,  source: true },
  gate:    { label: 'Gate',    icon: Lock,          cls: 'bg-red-600',     target: true,  source: true },
  wait:    { label: 'Esperar', icon: Clock,         cls: 'bg-slate-600',   target: true,  source: true },
  message: { label: 'Mensaje', icon: MessageSquare, cls: 'bg-teal-600',    target: true,  source: true },
  end:     { label: 'Fin',     icon: Square,        cls: 'bg-gray-700',    target: true,  source: false },
}

const handleCls = '!h-1.5 !w-1.5 !bg-gray-400 !border-0'

function makeNode(def: NodeDef) {
  const Comp = ({ data, selected }: NodeProps) => {
    const Icon = def.icon
    return (
      <div
        className={`rounded px-1.5 py-0.5 text-white shadow ${def.cls} ${
          selected ? 'ring-1 ring-amber-300' : ''
        }`}
        style={{ minWidth: 66 }}
      >
        {def.target && <Handle type="target" position={Position.Top} className={handleCls} />}
        <div className="flex items-center gap-1 text-[9px] font-semibold leading-tight">
          <Icon className="h-2.5 w-2.5 shrink-0" />
          <span className="truncate">{data?.label || def.label}</span>
        </div>
        {data?.subtitle && (
          <div className="truncate text-[8px] leading-tight text-white/70">{data.subtitle}</div>
        )}
        {def.source && <Handle type="source" position={Position.Bottom} className={handleCls} />}
      </div>
    )
  }
  return memo(Comp)
}

// Condición = rombo (rotación 45°), con contenido recto. Handle superior = entrada,
// inferior = rama "sí", derecho = rama "no".
const ConditionNode = memo(({ data, selected }: NodeProps) => (
  <div className="relative" style={{ width: 54, height: 54 }}>
    <div
      className={`absolute inset-1 rotate-45 rounded bg-amber-500 shadow ${
        selected ? 'ring-1 ring-amber-300' : ''
      }`}
    />
    <Handle type="target" position={Position.Top} className={handleCls} />
    <div className="absolute inset-0 flex flex-col items-center justify-center text-white">
      <GitBranch className="h-2.5 w-2.5" />
      <span className="max-w-[44px] truncate text-center text-[8px] font-semibold leading-tight">
        {data?.label || 'Condición'}
      </span>
    </div>
    <Handle id="yes" type="source" position={Position.Bottom} className={handleCls} />
    <Handle id="no" type="source" position={Position.Right} className={handleCls} />
  </div>
))
ConditionNode.displayName = 'ConditionNode'

export const nodeTypes = {
  start: makeNode(NODE_DEFS.start),
  action: makeNode(NODE_DEFS.action),
  llm: makeNode(NODE_DEFS.llm),
  gate: makeNode(NODE_DEFS.gate),
  wait: makeNode(NODE_DEFS.wait),
  message: makeNode(NODE_DEFS.message),
  end: makeNode(NODE_DEFS.end),
  condition: ConditionNode,
}

// Paleta (panel derecho del lienzo). Orden pensado para el flujo típico.
export const PALETTE: { type: string; label: string; icon: LucideIcon }[] = [
  { type: 'start',     label: 'Inicio',     icon: Play },
  { type: 'action',    label: 'Acción',     icon: Zap },
  { type: 'condition', label: 'Condición',  icon: GitBranch },
  { type: 'llm',       label: 'LLM',        icon: Bot },
  { type: 'gate',      label: 'Gate (auth)',icon: Lock },
  { type: 'wait',      label: 'Esperar',    icon: Clock },
  { type: 'message',   label: 'Mensaje',    icon: MessageSquare },
  { type: 'end',       label: 'Fin',        icon: Square },
]

export const DEFAULT_LABEL: Record<string, string> = Object.fromEntries(
  PALETTE.map((p) => [p.type, p.label]),
)
