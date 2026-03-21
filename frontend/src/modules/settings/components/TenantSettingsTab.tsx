import { useState, useEffect } from 'react'
import { Loader2, Save, Mail } from 'lucide-react'
import { useTenant, useUpdateTenantSendGrid } from '@/shared/hooks/useTenant'

export function TenantSettingsTab() {
  const { data: tenant, isLoading, error } = useTenant()
  const updateSendGrid = useUpdateTenantSendGrid()

  const [sendGridApiKey, setSendGridApiKey] = useState('')
  const [senderEmail, setSenderEmail] = useState('')

  useEffect(() => {
    if (tenant) {
      setSendGridApiKey('')
      setSenderEmail(tenant.senderEmail ?? '')
    }
  }, [tenant])

  const handleSaveSendGrid = () => {
    updateSendGrid.mutate({
      sendGridApiKey: sendGridApiKey || null,
      senderEmail: senderEmail || null,
    })
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12">
        <Loader2 className="h-6 w-6 animate-spin text-gray-400" />
      </div>
    )
  }

  if (error || !tenant) {
    return (
      <div className="rounded-lg bg-red-50 p-4 text-sm text-red-600">
        Error al cargar la informacion del tenant.
      </div>
    )
  }

  const inputClass = "mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none focus:ring-1 focus:ring-blue-500"

  return (
    <div className="space-y-6">
      {/* Info del tenant */}
      <div className="rounded-lg bg-white p-5 shadow-sm">
        <h3 className="mb-4 text-sm font-semibold text-gray-900">Informacion del tenant</h3>
        <div className="space-y-4">
          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-medium text-gray-500">Nombre</label>
              <p className="mt-1 text-sm text-gray-900">{tenant.name}</p>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500">Slug</label>
              <p className="mt-1 text-sm text-gray-900">{tenant.slug}</p>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500">Pais</label>
              <p className="mt-1 text-sm text-gray-900">{tenant.country || '—'}</p>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500">Proveedor WhatsApp</label>
              <p className="mt-1 text-sm text-gray-900">{tenant.whatsAppProvider}</p>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500">Telefono WhatsApp</label>
              <p className="mt-1 text-sm text-gray-900">{tenant.whatsAppPhoneNumber || '—'}</p>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500">Horario de atencion</label>
              <p className="mt-1 text-sm text-gray-900">{tenant.businessHoursStart} - {tenant.businessHoursEnd}</p>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500">Zona horaria</label>
              <p className="mt-1 text-sm text-gray-900">{tenant.timeZone}</p>
            </div>
            <div>
              <label className="block text-xs font-medium text-gray-500">Estado</label>
              <span className={`mt-1 inline-block rounded-full px-2 py-0.5 text-xs font-medium ${tenant.isActive ? 'bg-green-100 text-green-700' : 'bg-red-100 text-red-700'}`}>
                {tenant.isActive ? 'Activo' : 'Inactivo'}
              </span>
            </div>
          </div>
          <p className="text-xs text-gray-400">
            La configuracion del tenant se administra desde el panel de administrador.
          </p>
        </div>
      </div>

      {/* Configuración SendGrid */}
      <div className="rounded-lg bg-white p-5 shadow-sm">
        <div className="mb-4 flex items-center gap-2">
          <Mail className="h-4 w-4 text-blue-600" />
          <h3 className="text-sm font-semibold text-gray-900">Configuracion de Email (SendGrid)</h3>
        </div>
        <p className="mb-4 text-xs text-gray-500">
          Configura el token de SendGrid y la cuenta de correo para el envio de emails desde las campanas de este tenant.
        </p>
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-sm font-medium text-gray-700">Cuenta de correo remitente</label>
            <input
              type="email"
              value={senderEmail}
              onChange={e => setSenderEmail(e.target.value)}
              className={inputClass}
              placeholder="cobros@empresa.com"
            />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700">Token SendGrid</label>
            <input
              type="password"
              value={sendGridApiKey}
              onChange={e => setSendGridApiKey(e.target.value)}
              className={inputClass}
              placeholder={tenant.sendGridApiKey ? `Configurado (${tenant.sendGridApiKey})` : 'SG.xxxxx'}
            />
          </div>
        </div>
        <div className="mt-4">
          <button
            type="button"
            onClick={handleSaveSendGrid}
            disabled={updateSendGrid.isPending}
            className="flex items-center gap-2 rounded-lg bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50 transition-colors"
          >
            {updateSendGrid.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            {updateSendGrid.isPending ? 'Guardando...' : 'Guardar configuracion'}
          </button>
          {updateSendGrid.isSuccess && (
            <p className="mt-2 text-sm text-green-600">Configuracion actualizada correctamente.</p>
          )}
          {updateSendGrid.isError && (
            <p className="mt-2 text-sm text-red-600">Error al actualizar la configuracion.</p>
          )}
        </div>
      </div>
    </div>
  )
}
