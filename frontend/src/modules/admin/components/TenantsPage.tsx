import { useState } from 'react'
import { Building2, Plus, Users, Pencil, Power } from 'lucide-react'
import { useAdminTenants, useUpdateTenant, type AdminTenant } from '@/modules/admin/hooks/useAdminTenants'
import { TenantFormModal } from './TenantFormModal'
import { TenantUsersModal } from './TenantUsersModal'

export function TenantsPage() {
  const { data: tenants, isLoading } = useAdminTenants()
  const updateTenant = useUpdateTenant()

  const [showForm, setShowForm] = useState(false)
  const [editTenant, setEditTenant] = useState<AdminTenant | null>(null)
  const [usersTenant, setUsersTenant] = useState<AdminTenant | null>(null)

  const handleToggleActive = (tenant: AdminTenant) => {
    if (!confirm(tenant.isActive
      ? `Deshabilitar "${tenant.name}"? Todos sus usuarios seran deshabilitados.`
      : `Habilitar "${tenant.name}"?`))
      return

    updateTenant.mutate({ id: tenant.id, isActive: !tenant.isActive })
  }

  return (
    <div>
      <div className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold text-gray-900">Clientes</h1>
          <p className="text-sm text-gray-500">Administracion de tenants y organizaciones</p>
        </div>
        <button
          onClick={() => { setEditTenant(null); setShowForm(true) }}
          className="flex items-center gap-2 rounded-lg bg-amber-500 px-4 py-2 text-sm font-medium text-gray-900 hover:bg-amber-400 disabled:opacity-50 transition-colors"
        >
          <Plus className="h-4 w-4" />
          Nuevo Cliente
        </button>
      </div>

      {isLoading ? (
        <p className="text-sm text-gray-500">Cargando...</p>
      ) : !tenants?.length ? (
        <div className="rounded-lg bg-white p-8 text-center shadow-sm">
          <Building2 className="mx-auto h-12 w-12 text-gray-300" />
          <p className="mt-3 text-sm text-gray-500">No hay clientes registrados.</p>
        </div>
      ) : (
        <div className="overflow-hidden rounded-lg bg-white shadow-sm">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Nombre</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Pais</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Monto Mensual</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Estado</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Usuarios</th>
                <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Acciones</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-gray-200">
              {tenants.map((t) => (
                <tr key={t.id} className="hover:bg-gray-50">
                  <td className="px-4 py-3">
                    <p className="text-sm font-medium text-gray-900">{t.name}</p>
                    <p className="text-xs text-gray-400">{t.slug}</p>
                  </td>
                  <td className="px-4 py-3 text-sm text-gray-600">{t.country || '-'}</td>
                  <td className="px-4 py-3 text-sm text-gray-600">
                    ${t.monthlyBillingAmount.toFixed(2)}
                  </td>
                  <td className="px-4 py-3">
                    <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${
                      t.isActive ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'
                    }`}>
                      {t.isActive ? 'Activo' : 'Inactivo'}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-sm text-gray-600">{t.userCount}</td>
                  <td className="px-4 py-3">
                    <div className="flex gap-1">
                      <button
                        onClick={() => { setEditTenant(t); setShowForm(true) }}
                        className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors"
                        title="Editar"
                      >
                        <Pencil className="h-4 w-4" />
                      </button>
                      <button
                        onClick={() => setUsersTenant(t)}
                        className="rounded-lg p-1.5 text-gray-400 hover:bg-gray-100 hover:text-blue-600 transition-colors"
                        title="Usuarios"
                      >
                        <Users className="h-4 w-4" />
                      </button>
                      <button
                        onClick={() => handleToggleActive(t)}
                        className={`rounded-lg p-1.5 hover:bg-gray-100 transition-colors ${
                          t.isActive ? 'text-gray-400 hover:text-red-600' : 'text-gray-400 hover:text-green-600'
                        }`}
                        title={t.isActive ? 'Deshabilitar' : 'Habilitar'}
                      >
                        <Power className="h-4 w-4" />
                      </button>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {showForm && (
        <TenantFormModal
          tenant={editTenant ?? undefined}
          onClose={() => setShowForm(false)}
        />
      )}

      {usersTenant && (
        <TenantUsersModal
          tenant={usersTenant}
          onClose={() => setUsersTenant(null)}
        />
      )}
    </div>
  )
}
