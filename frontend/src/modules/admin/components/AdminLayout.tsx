import { useState, useEffect } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import {
  Building2, Bot, Tag, Phone, Users, LogOut, Zap, FileText,
  Calendar, TrendingDown, PanelLeftClose, PanelLeft, Inbox, Send,
} from 'lucide-react'
import { useSuperAdminStore } from '@/shared/stores/superAdminStore'

const adminNav = [
  { to: '/admin/tenants',         icon: Building2,   label: 'Tenants'         },
  { to: '/admin/inbox',           icon: Inbox,       label: 'Inbox monitor'   },
  { to: '/admin/outbox',          icon: Send,        label: 'Mensajes enviados' },
  { to: '/admin/agent-templates', icon: Bot,         label: 'Agentes'         },
  { to: '/admin/categories',      icon: Tag,         label: 'Categorías'      },
  { to: '/admin/whatsapp',        icon: Phone,       label: 'WhatsApp'        },
  { to: '/admin/actions',         icon: Zap,         label: 'Acciones'        },
  { to: '/admin/morosidad',       icon: TrendingDown,label: 'Descargas'       },
  { to: '/admin/scheduled-jobs',  icon: Calendar,    label: 'Scheduled Jobs'  },
  { to: '/admin/prompts',         icon: FileText,    label: 'Prompts'         },
  { to: '/admin/users',           icon: Users,       label: 'Administradores' },
]

const COLLAPSE_STORAGE_KEY = 'admin.sidebar.collapsed'

export function AdminLayout() {
  const navigate = useNavigate()
  const logout = useSuperAdminStore((s) => s.logout)

  // Persistencia: el estado del sidebar (colapsado/expandido) se guarda en
  // localStorage para que el admin no tenga que volverlo a colapsar en cada
  // navegación o login.
  const [collapsed, setCollapsed] = useState<boolean>(() => {
    if (typeof window === 'undefined') return false
    return window.localStorage.getItem(COLLAPSE_STORAGE_KEY) === '1'
  })

  useEffect(() => {
    if (typeof window === 'undefined') return
    window.localStorage.setItem(COLLAPSE_STORAGE_KEY, collapsed ? '1' : '0')
  }, [collapsed])

  const handleLogout = () => {
    logout()
    navigate('/admin/login', { replace: true })
  }

  return (
    <div className="flex h-screen">
      <aside
        className={`flex flex-col border-r border-gray-700 bg-gray-900 transition-all duration-200 ${
          collapsed ? 'w-16' : 'w-56'
        }`}
      >
        {/* Logo + nombre del panel */}
        <div className="flex h-14 items-center gap-2 border-b border-gray-700 px-4">
          <img src="/jamcst.png" alt="JamCST" className="h-8 w-8 shrink-0 rounded" />
          {!collapsed && <span className="text-sm font-bold text-white">Admin Panel</span>}
        </div>

        {/* Toggle */}
        <button
          onClick={() => setCollapsed((c) => !c)}
          title={collapsed ? 'Expandir menú' : 'Colapsar menú'}
          className="flex items-center justify-end px-4 py-2 text-gray-500 hover:text-gray-200"
        >
          {collapsed
            ? <PanelLeft className="h-4 w-4" />
            : <PanelLeftClose className="h-4 w-4" />}
        </button>

        {/* Nav */}
        <nav className="flex-1 space-y-1 px-2">
          {adminNav.map(({ to, icon: Icon, label }) => (
            <NavLink
              key={to}
              to={to}
              title={collapsed ? label : undefined}
              className={({ isActive }) =>
                `flex items-center gap-2.5 rounded-md px-3 py-2 text-sm font-medium transition-colors ${
                  collapsed ? 'justify-center' : ''
                } ${
                  isActive
                    ? 'bg-gray-800 text-amber-400'
                    : 'text-gray-400 hover:bg-gray-800 hover:text-gray-200'
                }`
              }
            >
              <Icon className="h-4 w-4 shrink-0" />
              {!collapsed && <span>{label}</span>}
            </NavLink>
          ))}
        </nav>

        {/* Logout */}
        <div className="border-t border-gray-700 p-3">
          <button
            onClick={handleLogout}
            title={collapsed ? 'Cerrar sesión' : undefined}
            className={`flex w-full items-center gap-2 rounded-md px-3 py-2 text-sm text-gray-400 hover:bg-gray-800 hover:text-gray-200 ${
              collapsed ? 'justify-center' : ''
            }`}
          >
            <LogOut className="h-4 w-4 shrink-0" />
            {!collapsed && <span>Cerrar sesión</span>}
          </button>
        </div>
      </aside>

      <main className="flex-1 overflow-auto bg-gray-50 p-6">
        <Outlet />
      </main>
    </div>
  )
}
