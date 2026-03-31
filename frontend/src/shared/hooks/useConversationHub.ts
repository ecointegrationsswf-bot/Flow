import { useEffect, useRef, useCallback } from 'react'
import * as signalR from '@microsoft/signalr'

export type HubMessage = {
  conversationId: string
  clientPhone: string
  clientName?: string
  agentType: string
  content: string
  direction: 'inbound' | 'outbound'
  sentAt: string
}

export function useConversationHub(
  tenantId: string,
  onMessage: (msg: HubMessage) => void,
  onEscalation: (conversationId: string) => void,
) {
  const hubRef = useRef<signalR.HubConnection | null>(null)

  useEffect(() => {
    if (!tenantId) return

    // Construir URL absoluta del hub usando la misma base que el API.
    // En producción VITE_API_BASE_URL = "http://api-server/api" → hub = "http://api-server/hubs/conversations"
    // En dev sin env var: usa URL relativa (el proxy de Vite la redirige a localhost:5000)
    const apiBase = import.meta.env.VITE_API_BASE_URL as string | undefined
    const hubUrl = apiBase
      ? apiBase.replace(/\/api\/?$/, '') + '/hubs/conversations'
      : '/hubs/conversations'

    const hub = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl, {
        accessTokenFactory: () => localStorage.getItem('token') ?? '',
        transport: signalR.HttpTransportType.LongPolling,
      })
      .withAutomaticReconnect()
      .build()

    hub.on('MessageReceived', onMessage)
    hub.on('AgentReplied', onMessage)
    hub.on('ConversationEscalated', onEscalation)
    hub.on('ConversationUpdated', onMessage)
    hub.on('ConversationClosed', onEscalation)

    hub.start()
      .then(() => {
        console.log('[SignalR] Conectado, uniendo grupo tenant:', tenantId)
        hub.invoke('JoinTenantGroup', tenantId)
      })
      .catch((err) => console.error('[SignalR] Error al conectar:', err))

    hubRef.current = hub
    return () => { hub.stop() }
  }, [tenantId])

  const joinConversation = useCallback((conversationId: string) => {
    hubRef.current?.invoke('JoinConversation', conversationId)
  }, [])

  return { hubRef, joinConversation }
}
