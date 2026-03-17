import { Routes, Route, Navigate } from 'react-router-dom'
import { useEffect } from 'react'
import { useAuthStore } from '@/shared/stores/authStore'
import { AppLayout } from '@/shared/components/AppLayout'
import { ProtectedRoute } from '@/shared/components/ProtectedRoute'
import { LoginPage } from '@/modules/auth/components/LoginPage'
import { DashboardPage } from '@/modules/dashboard/components/DashboardPage'
import { MonitorPage } from '@/modules/monitor/components/MonitorPage'
import { CampaignsPage } from '@/modules/campaigns/components/CampaignsPage'
import { CampaignUploadPage } from '@/modules/campaigns/components/CampaignUploadPage'
import { AgentsListPage } from '@/modules/agents/components/AgentsListPage'
import { AgentFormPage } from '@/modules/agents/components/AgentFormPage'
import { SettingsPage } from '@/modules/settings/components/SettingsPage'
import { LabelsPage } from '@/modules/labels/components/LabelsPage'

export default function App() {
  const hydrate = useAuthStore((s) => s.hydrate)
  useEffect(() => { hydrate() }, [hydrate])

  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />

      <Route element={<ProtectedRoute />}>
        <Route element={<AppLayout />}>
          <Route path="/" element={<Navigate to="/dashboard" replace />} />
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route path="/monitor" element={<MonitorPage />} />
          <Route path="/campaigns" element={<CampaignsPage />} />
          <Route path="/campaigns/new" element={<CampaignUploadPage />} />
          <Route path="/agents" element={<AgentsListPage />} />
          <Route path="/agents/new" element={<AgentFormPage />} />
          <Route path="/agents/:id/edit" element={<AgentFormPage />} />
          <Route path="/labels" element={<LabelsPage />} />
          <Route path="/settings" element={<SettingsPage />} />
        </Route>
      </Route>

      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  )
}
