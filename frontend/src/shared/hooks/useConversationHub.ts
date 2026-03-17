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

    const hub = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/conversations', {
        accessTokenFactory: () => localStorage.getItem('token') ?? '',
      })
      .withAutomaticReconnect()
      .build()

    hub.on('MessageReceived', onMessage)
    hub.on('AgentReplied', onMessage)
    hub.on('ConversationEscalated', onEscalation)
    hub.on('ConversationUpdated', onMessage)
    hub.on('ConversationClosed', onEscalation)

    hub.start().then(() => hub.invoke('JoinTenantGroup', tenantId))

    hubRef.current = hub
    return () => { hub.stop() }
  }, [tenantId])

  const joinConversation = useCallback((conversationId: string) => {
    hubRef.current?.invoke('JoinConversation', conversationId)
  }, [])

  return { hubRef, joinConversation }
}
