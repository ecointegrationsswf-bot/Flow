import { useCallback, useMemo, useState, type ReactNode, type DragEvent } from 'react'
import ReactFlow, {
  Background, Controls, MiniMap, addEdge, useNodesState, useEdgesState,
  ReactFlowProvider, useReactFlow, MarkerType,
  type Node, type Edge, type Connection,
} from 'reactflow'
import 'reactflow/dist/style.css'
import { ArrowLeft, Save, Trash2, Plus, Settings } from 'lucide-react'
import { nodeTypes, PALETTE, DEFAULT_LABEL } from './flowNodes'
import { useUpdateFlow, type FlowDetail } from '../../hooks/useAdminFlows'
import {
  useAdminTenantActionsConfig, useAdminUpdateTenantActionContract,
  type TenantActionConfig,
} from '../../hooks/useAdminTenantActionsConfig'
import { WebhookBuilderModal } from '@/modules/webhookBuilder/components/WebhookBuilderModal'
import { parseContract } from '@/shared/utils/parseContract'
import { toast } from '@/shared/components/dialog'

interface ParsedGraph { nodes: Node[]; edges: Edge[] }

function parseGraph(flowJson: string): ParsedGraph {
  try {
    const g = JSON.parse(flowJson || '{}')
    return {
      nodes: Array.isArray(g.nodes) ? g.nodes : [],
      edges: Array.isArray(g.edges) ? g.edges : [],
    }
  } catch {
    return { nodes: [], edges: [] }
  }
}

const OPERATORS = ['equals', 'notEquals', 'contains', 'startsWith', 'isNotNull', 'isNull', 'gt', 'gte', 'lt', 'lte']
const DND_MIME = 'application/reactflow'

// crypto.randomUUID() SOLO existe en contextos seguros (HTTPS/localhost). El portal corre
// en HTTP → en producción la función es undefined y agregar un nodo explotaba silenciosamente
// (ni drag-and-drop ni click agregaban nada). Fallback: id único sin crypto.
function genNodeId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID()
  return `n_${Date.now().toString(36)}_${Math.random().toString(36).slice(2, 10)}`
}

// El provider debe envolver al editor para que useReactFlow() (screenToFlowPosition) funcione.
export function FlowCanvas({ flow, onBack }: { flow: FlowDetail; onBack: () => void }) {
  return (
    <ReactFlowProvider>
      <FlowEditor flow={flow} onBack={onBack} />
    </ReactFlowProvider>
  )
}

function FlowEditor({ flow, onBack }: { flow: FlowDetail; onBack: () => void }) {
  const initial = useMemo(() => parseGraph(flow.flowJson), [flow.flowJson])
  const [nodes, setNodes, onNodesChange] = useNodesState(initial.nodes)
  const [edges, setEdges, onEdgesChange] = useEdgesState(initial.edges)
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [name, setName] = useState(flow.name)
  const [active, setActive] = useState(flow.isActive)
  const { screenToFlowPosition } = useReactFlow()
  const update = useUpdateFlow()

  // Acciones del tenant (para el dropdown del nodo Acción) + edición de su contrato webhook.
  const tenantActions = useAdminTenantActionsConfig(flow.tenantId)
  const updateContract = useAdminUpdateTenantActionContract(flow.tenantId)
  const [webhookActionId, setWebhookActionId] = useState<string | null>(null)
  const webhookAction = tenantActions.data?.find((a) => a.id === webhookActionId) ?? null

  const onConnect = useCallback(
    (c: Connection) => setEdges((eds) => addEdge({ ...c, markerEnd: { type: MarkerType.ArrowClosed } }, eds)),
    [setEdges],
  )

  const addNodeAt = useCallback(
    (type: string, position: { x: number; y: number }) => {
      const id = genNodeId()
      setNodes((nds) => nds.concat({ id, type, position, data: { label: DEFAULT_LABEL[type] ?? type } }))
      setSelectedId(id)
    },
    [setNodes],
  )

  // Click en la paleta = agrega al centro del lienzo.
  const addNode = useCallback(
    (type: string) => {
      let pos = { x: 200, y: 150 }
      try {
        pos = screenToFlowPosition({ x: window.innerWidth / 2 - 140, y: window.innerHeight / 2 })
      } catch { /* fallback al default */ }
      addNodeAt(type, pos)
    },
    [addNodeAt, screenToFlowPosition],
  )

  // Drag-and-drop desde la paleta.
  const onDragStartPalette = useCallback((e: DragEvent, type: string) => {
    e.dataTransfer.setData(DND_MIME, type)
    // Fallback text/plain: algunos navegadores no inician el drag (o lo descartan)
    // si el dataTransfer solo trae un MIME custom.
    e.dataTransfer.setData('text/plain', type)
    e.dataTransfer.effectAllowed = 'move'
  }, [])

  const onDragOver = useCallback((e: DragEvent) => {
    e.preventDefault()
    e.dataTransfer.dropEffect = 'move'
  }, [])

  const onDrop = useCallback(
    (e: DragEvent) => {
      e.preventDefault()
      const type = e.dataTransfer.getData(DND_MIME) || e.dataTransfer.getData('text/plain')
      if (!type) return
      let position = { x: 200, y: 150 }
      try {
        position = screenToFlowPosition({ x: e.clientX, y: e.clientY })
      } catch { /* fallback al default */ }
      addNodeAt(type, position)
    },
    [addNodeAt, screenToFlowPosition],
  )

  const selected = nodes.find((n) => n.id === selectedId) ?? null

  const patchData = useCallback(
    (patch: Record<string, unknown>) => {
      if (!selectedId) return
      setNodes((nds) => nds.map((n) => (n.id === selectedId ? { ...n, data: { ...n.data, ...patch } } : n)))
    },
    [selectedId, setNodes],
  )

  const deleteSelected = useCallback(() => {
    if (!selectedId) return
    setNodes((nds) => nds.filter((n) => n.id !== selectedId))
    setEdges((eds) => eds.filter((e) => e.source !== selectedId && e.target !== selectedId))
    setSelectedId(null)
  }, [selectedId, setNodes, setEdges])

  const handleSave = useCallback(async () => {
    const clean = {
      nodes: nodes.map((n) => ({ id: n.id, type: n.type, position: n.position, data: n.data })),
      edges: edges.map((e) => ({ id: e.id, source: e.source, target: e.target, sourceHandle: e.sourceHandle, label: e.label })),
    }
    try {
      await update.mutateAsync({ id: flow.id, name: name.trim() || flow.name, flowJson: JSON.stringify(clean), isActive: active })
      toast.success('Flujo guardado')
    } catch {
      /* el interceptor global ya muestra el error */
    }
  }, [nodes, edges, update, flow.id, flow.name, name, active])

  return (
    <div className="flex h-[calc(100vh-3rem)] min-h-0 flex-col">
      {/* Toolbar */}
      <div className="flex items-center gap-3 border-b border-gray-200 bg-white px-4 py-2">
        <button onClick={onBack} className="flex items-center gap-1 rounded-md px-2 py-1 text-sm text-gray-600 hover:bg-gray-100">
          <ArrowLeft className="h-4 w-4" /> Volver
        </button>
        <input
          value={name}
          onChange={(e) => setName(e.target.value)}
          className="flex-1 rounded-md border border-gray-300 px-3 py-1.5 text-sm font-semibold"
          placeholder="Nombre del flujo"
        />
        <label className="flex items-center gap-1.5 text-sm text-gray-600">
          <input type="checkbox" checked={active} onChange={(e) => setActive(e.target.checked)} /> Activo
        </label>
        <button
          onClick={handleSave}
          disabled={update.isPending}
          className="flex items-center gap-1.5 rounded-md bg-amber-500 px-3 py-1.5 text-sm font-semibold text-white hover:bg-amber-600 disabled:opacity-50"
        >
          <Save className="h-4 w-4" /> {update.isPending ? 'Guardando…' : 'Guardar'}
        </button>
      </div>

      <div className="flex min-h-0 flex-1 overflow-hidden">
        {/* Lienzo */}
        <div className="relative min-h-0 flex-1" onDrop={onDrop} onDragOver={onDragOver}>
          <ReactFlow
            nodes={nodes}
            edges={edges}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={onConnect}
            onNodeClick={(_e, n) => setSelectedId(n.id)}
            onPaneClick={() => setSelectedId(null)}
            nodeTypes={nodeTypes}
            nodesDraggable
            deleteKeyCode={['Backspace', 'Delete']}
            fitView
          >
            <Background gap={16} color="#e5e7eb" />
            <Controls />
            <MiniMap pannable zoomable />
          </ReactFlow>
        </div>

        {/* Panel derecho: paleta + config */}
        <aside className="w-72 shrink-0 overflow-y-auto border-l border-gray-200 bg-white p-3">
          <div className="mb-2 text-xs font-bold uppercase tracking-wide text-gray-500">Nodos</div>
          <p className="mb-2 text-[10px] text-gray-400">Arrastrá un nodo al lienzo (o hacé clic para agregarlo).</p>
          <div className="grid grid-cols-2 gap-2">
            {PALETTE.map(({ type, label, icon: Icon }) => (
              <button
                key={type}
                draggable
                onDragStart={(e) => onDragStartPalette(e, type)}
                onClick={() => addNode(type)}
                className="flex cursor-grab items-center gap-1.5 rounded-md border border-gray-200 px-2 py-2 text-xs font-medium text-gray-700 hover:border-amber-400 hover:bg-amber-50 active:cursor-grabbing"
              >
                <Icon className="h-3.5 w-3.5 shrink-0 text-gray-500" />
                <span className="truncate">{label}</span>
                <Plus className="ml-auto h-3 w-3 text-gray-300" />
              </button>
            ))}
          </div>

          <div className="my-3 border-t border-gray-200" />

          {selected ? (
            <NodeConfig
              key={selected.id}
              node={selected}
              onPatch={patchData}
              onDelete={deleteSelected}
              actions={tenantActions.data ?? []}
              onOpenWebhook={setWebhookActionId}
            />
          ) : (
            <p className="text-xs text-gray-400">Hacé clic en un nodo para configurarlo. Arrastrá entre los puntos para conectarlos. Para mover un nodo, arrastralo por su cuerpo.</p>
          )}
        </aside>
      </div>

      {/* Modal de configuración del webhook de la acción seleccionada */}
      {webhookAction && (
        <WebhookBuilderModal
          initial={parseContract(webhookAction.defaultWebhookContract)}
          actionName={webhookAction.name}
          onSave={(bundle) => {
            updateContract.mutate({ actionId: webhookAction.id, contract: JSON.stringify(bundle) })
            toast.success('Contrato del webhook guardado')
            setWebhookActionId(null)
          }}
          onClose={() => setWebhookActionId(null)}
        />
      )}
    </div>
  )
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    <label className="mb-2 block">
      <span className="mb-0.5 block text-[11px] font-semibold text-gray-500">{label}</span>
      {children}
    </label>
  )
}

const inputCls = 'w-full rounded-md border border-gray-300 px-2 py-1.5 text-sm'

function NodeConfig({ node, onPatch, onDelete, actions, onOpenWebhook }: {
  node: Node
  onPatch: (p: Record<string, unknown>) => void
  onDelete: () => void
  actions: TenantActionConfig[]
  onOpenWebhook: (actionId: string) => void
}) {
  const d = node.data ?? {}
  // Resolver la acción seleccionada por id o, retrocompat, por slug (name).
  const selectedActionId: string =
    d.actionId ?? actions.find((a) => a.name === d.actionSlug)?.id ?? ''
  return (
    <div>
      <div className="mb-2 flex items-center justify-between">
        <span className="text-xs font-bold uppercase tracking-wide text-gray-500">Config · {node.type}</span>
        <button onClick={onDelete} title="Eliminar nodo" className="rounded p-1 text-red-500 hover:bg-red-50">
          <Trash2 className="h-3.5 w-3.5" />
        </button>
      </div>

      <Field label="Etiqueta">
        <input className={inputCls} value={d.label ?? ''} onChange={(e) => onPatch({ label: e.target.value })} />
      </Field>

      {node.type === 'start' && (
        <label className="mb-2 flex items-start gap-2 text-xs text-gray-700">
          <input
            type="checkbox"
            className="mt-0.5"
            checked={!!d.isAuthEntry}
            onChange={(e) => onPatch({ isAuthEntry: e.target.checked })}
          />
          <span>
            Entrada de validación (2FA)
            <span className="block text-[10px] text-gray-400">Marca el inicio del sub-flujo de validación de identidad.</span>
          </span>
        </label>
      )}

      {node.type === 'action' && (
        <>
          <Field label="Acción">
            <select
              className={inputCls}
              value={selectedActionId}
              onChange={(e) => {
                const a = actions.find((x) => x.id === e.target.value)
                onPatch({
                  actionId: a?.id ?? null,
                  actionSlug: a?.name ?? null,
                  subtitle: a?.name ?? null,
                  label: !d.label || d.label === 'Acción' ? (a?.name ?? 'Acción') : d.label,
                })
              }}
            >
              <option value="">— elegí una acción —</option>
              {actions.map((a) => (
                <option key={a.id} value={a.id}>{a.name}</option>
              ))}
            </select>
          </Field>
          <label className="mb-2 flex items-start gap-2 text-xs text-gray-700">
            <input
              type="checkbox"
              className="mt-0.5"
              checked={!!d.requiresAuth}
              onChange={(e) => onPatch({ requiresAuth: e.target.checked })}
            />
            <span>
              Confidencial (requiere validación)
              <span className="block text-[10px] text-gray-400">El gate la bloquea si el cliente no está validado.</span>
            </span>
          </label>
          {selectedActionId && (
            <button
              type="button"
              onClick={() => onOpenWebhook(selectedActionId)}
              className="mb-2 flex w-full items-center justify-center gap-1.5 rounded-md border border-gray-300 px-2 py-1.5 text-xs font-medium text-gray-700 hover:border-amber-400 hover:bg-amber-50"
            >
              <Settings className="h-3.5 w-3.5" /> Ver / editar configuración (webhook)
            </button>
          )}
          {actions.length === 0 && (
            <p className="mb-2 text-[10px] text-gray-400">No hay acciones cargadas para este tenant.</p>
          )}
        </>
      )}

      {node.type === 'condition' && (
        <>
          <Field label="Path (ej: status, llm.intent, auth.isAuthenticated)">
            <input className={inputCls} value={d.path ?? ''} onChange={(e) => onPatch({ path: e.target.value })} />
          </Field>
          <Field label="Operador">
            <select className={inputCls} value={d.operator ?? 'equals'} onChange={(e) => onPatch({ operator: e.target.value })}>
              {OPERATORS.map((op) => <option key={op} value={op}>{op}</option>)}
            </select>
          </Field>
          <Field label="Valor">
            <input className={inputCls} value={d.value ?? ''} onChange={(e) => onPatch({ value: e.target.value })} />
          </Field>
          <p className="text-[10px] text-gray-400">Rama inferior = sí · rama derecha = no.</p>
        </>
      )}

      {node.type === 'llm' && (
        <Field label="Instrucción para el LLM">
          <textarea className={inputCls} rows={4} value={d.instruction ?? ''} onChange={(e) => onPatch({ instruction: e.target.value })} />
        </Field>
      )}

      {node.type === 'gate' && (
        <Field label="Mensaje si NO está autenticado">
          <textarea className={inputCls} rows={3} placeholder="Para esto necesito validar tu identidad…" value={d.authMessage ?? ''} onChange={(e) => onPatch({ authMessage: e.target.value })} />
        </Field>
      )}

      {node.type === 'wait' && (
        <Field label="Qué espera del cliente">
          <input className={inputCls} placeholder="el código / una respuesta" value={d.waitFor ?? ''} onChange={(e) => onPatch({ waitFor: e.target.value })} />
        </Field>
      )}

      {node.type === 'message' && (
        <Field label="Texto del mensaje">
          <textarea className={inputCls} rows={3} value={d.text ?? ''} onChange={(e) => onPatch({ text: e.target.value })} />
        </Field>
      )}
    </div>
  )
}
