import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { Building2, Bot, Tag, Phone, Users, LogOut, Shield, Zap, FileText, Calendar } from 'lucide-react'
import { useSuperAdminStore } from '@/shared/stores/superAdminStore'

const adminNav = [
  { to: '/admin/tenants', icon: Building2, label: 'Tenants' },
  { to: '/admin/agent-templates', icon: Bot, label: 'Agentes' },
  { to: '/admin/categories', icon: Tag, label: 'Categorias' },
  { to: '/admin/whatsapp', icon: Phone, label: 'WhatsApp' },
  { to: '/admin/actions', icon: Zap, label: 'Acciones' },
  { to: '/admin/scheduled-jobs', icon: Calendar, label: 'Scheduled Jobs' },
  { to: '/admin/prompts', icon: FileText, label: 'Prompts' },
  { to: '/admin/users', icon: Users, label: 'Administradores' },
]

export function AdminLayout() {
  const navigate = useNavigate()
  const logout = useSuperAdminStore((s) => s.logout)

  const handleLogout = () => {
    logout()
    navigate('/admin/login', { replace: true })
  }

  return (
    <div className="flex h-screen">
      <aside className="flex w-56 flex-col border-r border-gray-700 bg-gray-900">
        <div className="flex h-14 items-center gap-2 border-b border-gray-700 px-4">
          <img src="/jamcst.png" alt="JamCST" className="h-8 w-8 rounded" />
          <span className="text-sm font-bold text-white">Admin Panel</span>
        </div>

        <nav className="flex-1 space-y-1 px-2 py-3">
          {adminNav.map(({ to, icon: Icon, label }) => (
            <NavLink
              key={to}
              to={to}
              className={({ isActive }) =>
                `flex items-center gap-2.5 rounded-md px-3 py-2 text-sm font-medium transition-colors ${
                  isActive
                    ? 'bg-gray-800 text-amber-400'
                    : 'text-gray-400 hover:bg-gray-800 hover:text-gray-200'
                }`
              }
            >
              <Icon className="h-4 w-4" />
              <span>{label}</span>
            </NavLink>
          ))}
        </nav>

        <div className="border-t border-gray-700 p-3">
          <button
            onClick={handleLogout}
            className="flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm text-gray-400 hover:bg-gray-800 hover:text-gray-200"
          >
            <LogOut className="h-4 w-4" />
            <span>Cerrar sesion</span>
          </button>
        </div>
      </aside>

      <main className="flex-1 overflow-auto bg-gray-50 p-6">
        <Outlet />
      </main>
    </div>
  )
}
