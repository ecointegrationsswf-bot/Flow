import { useState } from 'react'
import { PageHeader } from '@/shared/components/PageHeader'
import { UsersTab } from './UsersTab'
import { WhatsAppTab } from './WhatsAppTab'

// El tab "Tenant" (configuración del tenant) se movió al panel del super admin
// en Admin → Tenants → Editar Cliente → tab "Configuración". Acá sólo quedan
// Usuarios (alta/baja de usuarios del propio tenant) y WhatsApp (estado del canal).
const tabs = [
  { id: 'users', label: 'Usuarios' },
  { id: 'whatsapp', label: 'WhatsApp' },
] as const

type TabId = (typeof tabs)[number]['id']

export function SettingsPage() {
  const [activeTab, setActiveTab] = useState<TabId>('users')

  return (
    <div>
      <PageHeader title="Configuracion" subtitle="Usuarios y canal de WhatsApp" />

      {/* Tabs */}
      <div className="mb-6 border-b border-gray-200">
        <nav className="-mb-px flex gap-6">
          {tabs.map((tab) => (
            <button
              key={tab.id}
              onClick={() => setActiveTab(tab.id)}
              className={`border-b-2 pb-3 text-sm font-medium transition-colors ${
                activeTab === tab.id
                  ? 'border-blue-600 text-blue-600'
                  : 'border-transparent text-gray-500 hover:border-gray-300 hover:text-gray-700'
              }`}
            >
              {tab.label}
            </button>
          ))}
        </nav>
      </div>

      {activeTab === 'users' && <UsersTab />}
      {activeTab === 'whatsapp' && <WhatsAppTab />}
    </div>
  )
}
