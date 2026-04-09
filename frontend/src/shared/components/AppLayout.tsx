import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import {
  LayoutDashboard, MessageSquare, Megaphone, Bot, ClipboardList,
  Settings, LogOut, PanelLeftClose, PanelLeft, Tag, User, Brain,
} from 'lucide-react'
import { useAuthStore } from '@/shared/stores/authStore'
import { useUiStore } from '@/shared/stores/uiStore'
import { useWhatsAppStatus } from '@/shared/hooks/useWhatsApp'

const navItems = [
  { to: '/dashboard', icon: LayoutDashboard, label: 'Dashboard' },
  { to: '/monitor', icon: MessageSquare, label: 'Monitor' },
  { to: '/campaign-templates', icon: ClipboardList, label: 'Maestro Campanas' },
  { to: '/campaigns', icon: Megaphone, label: 'Campanas' },
  { to: '/agents', icon: Bot, label: 'Agentes IA' },
  { to: '/labels', icon: Tag, label: 'Etiquetas' },
  { to: '/brain', icon: Brain, label: 'Cerebro' },
  { to: '/settings', icon: Settings, label: 'Configuracion' },
  { to: '/profile', icon: User, label: 'Mi Perfil' },
]

export function AppLayout() {
  const { user, logout } = useAuthStore()
  const { sidebarCollapsed, toggleSidebar } = useUiStore()
  const { data: waStatus } = useWhatsAppStatus()
  const navigate = useNavigate()
  const waConnected = waStatus?.status === 'authenticated'

  return (
    <div className="flex h-screen">
      {/* Sidebar */}
      <aside className={`flex flex-col border-r border-gray-200 bg-white transition-all duration-200 ${sidebarCollapsed ? 'w-16' : 'w-64'}`}>
        {/* Logo */}
        <div className="flex h-14 items-center gap-2 border-b border-gray-200 px-4">
          <img src="/logo.png" alt="TalkIA" className="h-8 w-8 shrink-0 rounded" />
          {!sidebarCollapsed && <span className="text-lg font-bold text-gray-900">TalkIA</span>}
        </div>

        {/* Toggle */}
        <button
          onClick={toggleSidebar}
          className="flex items-center justify-end px-4 py-2 text-gray-400 hover:text-gray-600"
        >
          {sidebarCollapsed ? <PanelLeft className="h-4 w-4" /> : <PanelLeftClose className="h-4 w-4" />}
        </button>

        {/* Nav */}
        <nav className="flex-1 space-y-1 px-2">
          {navItems.map(({ to, icon: Icon, label }) => (
            <NavLink
              key={to}
              to={to}
              className={({ isActive }) =>
                `flex items-center gap-3 rounded-md px-3 py-2 text-sm font-medium transition-colors ${
                  isActive
                    ? 'bg-blue-50 text-blue-700'
                    : 'text-gray-600 hover:bg-gray-50 hover:text-gray-900'
                }`
              }
            >
              <div className="relative shrink-0">
                <Icon className="h-5 w-5" />
                {to === '/settings' && (
                  <span
                    className={`absolute -right-0.5 -top-0.5 h-2.5 w-2.5 rounded-full border-2 border-white ${waConnected ? 'bg-green-500' : 'bg-red-500'}`}
                  />
                )}
              </div>
              {!sidebarCollapsed && <span>{label}</span>}
            </NavLink>
          ))}
        </nav>

        {/* User */}
        <div className="border-t border-gray-200 p-3">
          {!sidebarCollapsed && user && (
            <button
              onClick={() => navigate('/profile')}
              className="mb-2 flex w-full items-center gap-2.5 rounded-md px-1 py-1 text-left hover:bg-gray-50"
            >
              {user.avatarUrl ? (
                <img
                  src={user.avatarUrl}
                  alt={user.fullName}
                  className="h-8 w-8 shrink-0 rounded-full object-cover"
                />
              ) : (
                <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-blue-500 text-xs font-bold text-white">
                  {(user.fullName ?? '')
                    .split(' ')
                    .filter(w => w.length > 0)
                    .map(w => w[0])
                    .slice(0, 2)
                    .join('')
                    .toUpperCase() || '?'}
                </div>
              )}
              <div className="min-w-0">
                <p className="truncate text-sm font-medium text-gray-900">{user.fullName}</p>
                <p className="truncate text-xs text-gray-500">{user.role}</p>
              </div>
            </button>
          )}
          {sidebarCollapsed && user && (
            <button
              onClick={() => navigate('/profile')}
              className="mb-2 flex w-full items-center justify-center rounded-md py-1 hover:bg-gray-50"
            >
              {user.avatarUrl ? (
                <img
                  src={user.avatarUrl}
                  alt={user.fullName}
                  className="h-8 w-8 rounded-full object-cover"
                />
              ) : (
                <div className="flex h-8 w-8 items-center justify-center rounded-full bg-blue-500 text-xs font-bold text-white">
                  {(user.fullName ?? '')
                    .split(' ')
                    .filter(w => w.length > 0)
                    .map(w => w[0])
                    .slice(0, 2)
                    .join('')
                    .toUpperCase() || '?'}
                </div>
              )}
            </button>
          )}
          <button
            onClick={logout}
            className="flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm text-gray-600 hover:bg-gray-50 hover:text-gray-900"
          >
            <LogOut className="h-4 w-4 shrink-0" />
            {!sidebarCollapsed && <span>Cerrar sesion</span>}
          </button>
        </div>
      </aside>

      {/* Main */}
      <main className="flex-1 overflow-auto bg-gray-50 p-6">
        <Outlet />
      </main>
    </div>
  )
}
