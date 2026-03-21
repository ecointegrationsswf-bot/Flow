import { Routes, Route, Navigate } from 'react-router-dom'
import { useEffect } from 'react'
import { useAuthStore } from '@/shared/stores/authStore'
import { useSuperAdminStore } from '@/shared/stores/superAdminStore'
import { AppLayout } from '@/shared/components/AppLayout'
import { ProtectedRoute } from '@/shared/components/ProtectedRoute'
import { LoginPage } from '@/modules/auth/components/LoginPage'
import { DashboardPage } from '@/modules/dashboard/components/DashboardPage'
import { MonitorPage } from '@/modules/monitor/components/MonitorPage'
import { CampaignsPage } from '@/modules/campaigns/components/CampaignsPage'
import { CampaignUploadPage } from '@/modules/campaigns/components/CampaignUploadPage'
import { CampaignTemplatesPage } from '@/modules/campaigns/components/CampaignTemplatesPage'
import { CampaignTemplateFormPage } from '@/modules/campaigns/components/CampaignTemplateFormPage'
import { AgentsListPage } from '@/modules/agents/components/AgentsListPage'
import { AgentFormPage } from '@/modules/agents/components/AgentFormPage'
import { SettingsPage } from '@/modules/settings/components/SettingsPage'
import { LabelsPage } from '@/modules/labels/components/LabelsPage'
import { AdminLoginPage } from '@/modules/admin/components/AdminLoginPage'
import { AdminProtectedRoute } from '@/modules/admin/components/AdminProtectedRoute'
import { AdminLayout } from '@/modules/admin/components/AdminLayout'
import { TenantsPage } from '@/modules/admin/components/TenantsPage'
import { AgentTemplatesPage } from '@/modules/admin/components/AgentTemplatesPage'
import { AgentTemplateFormPage } from '@/modules/admin/components/AgentTemplateFormPage'
import { CategoriesPage } from '@/modules/admin/components/CategoriesPage'
import { AdminWhatsAppPage } from '@/modules/admin/components/AdminWhatsAppPage'
import { AdminUsersPage } from '@/modules/admin/components/AdminUsersPage'
import { ActionsPage } from '@/modules/admin/components/ActionsPage'
import { PromptsPage } from '@/modules/admin/components/PromptsPage'
import { PromptFormPage } from '@/modules/admin/components/PromptFormPage'
import { ProfilePage } from '@/modules/profile/components/ProfilePage'
import { ForgotPasswordPage } from '@/modules/auth/components/ForgotPasswordPage'
import { ResetPasswordPage } from '@/modules/auth/components/ResetPasswordPage'

export default function App() {
  const hydrate = useAuthStore((s) => s.hydrate)
  const hydrateSa = useSuperAdminStore((s) => s.hydrate)
  useEffect(() => { hydrate(); hydrateSa() }, [hydrate, hydrateSa])

  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/reset-password" element={<ResetPasswordPage />} />

      {/* Super Admin routes */}
      <Route path="/admin/login" element={<AdminLoginPage />} />
      <Route path="/admin" element={<AdminProtectedRoute />}>
        <Route element={<AdminLayout />}>
          <Route index element={<Navigate to="/admin/tenants" replace />} />
          <Route path="tenants" element={<TenantsPage />} />
          <Route path="agent-templates" element={<AgentTemplatesPage />} />
          <Route path="agent-templates/new" element={<AgentTemplateFormPage />} />
          <Route path="agent-templates/:id/edit" element={<AgentTemplateFormPage />} />
          <Route path="categories" element={<CategoriesPage />} />
          <Route path="whatsapp" element={<AdminWhatsAppPage />} />
          <Route path="actions" element={<ActionsPage />} />
          <Route path="prompts" element={<PromptsPage />} />
          <Route path="prompts/new" element={<PromptFormPage />} />
          <Route path="prompts/:id/edit" element={<PromptFormPage />} />
          <Route path="users" element={<AdminUsersPage />} />
        </Route>
      </Route>

      <Route element={<ProtectedRoute />}>
        <Route element={<AppLayout />}>
          <Route path="/" element={<Navigate to="/dashboard" replace />} />
          <Route path="/dashboard" element={<DashboardPage />} />
          <Route path="/monitor" element={<MonitorPage />} />
          <Route path="/campaign-templates" element={<CampaignTemplatesPage />} />
          <Route path="/campaign-templates/new" element={<CampaignTemplateFormPage />} />
          <Route path="/campaign-templates/:id/edit" element={<CampaignTemplateFormPage />} />
          <Route path="/campaigns" element={<CampaignsPage />} />
          <Route path="/campaigns/new" element={<CampaignUploadPage />} />
          <Route path="/agents" element={<AgentsListPage />} />
          <Route path="/agents/new" element={<AgentFormPage />} />
          <Route path="/agents/:id/edit" element={<AgentFormPage />} />
          <Route path="/labels" element={<LabelsPage />} />
          <Route path="/settings" element={<SettingsPage />} />
          <Route path="/profile" element={<ProfilePage />} />
        </Route>
      </Route>

      <Route path="*" element={<Navigate to="/dashboard" replace />} />
    </Routes>
  )
}
