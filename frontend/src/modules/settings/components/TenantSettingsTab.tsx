export function TenantSettingsTab() {
  return (
    <div className="rounded-lg bg-white p-5 shadow-sm">
      <h3 className="mb-4 text-sm font-semibold text-gray-900">Informacion del tenant</h3>
      <div className="space-y-4">
        <div className="grid grid-cols-2 gap-4">
          <div>
            <label className="block text-xs font-medium text-gray-500">Nombre</label>
            <p className="mt-1 text-sm text-gray-900">Somos Seguros</p>
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-500">Slug</label>
            <p className="mt-1 text-sm text-gray-900">somos-seguros</p>
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-500">Proveedor WhatsApp</label>
            <p className="mt-1 text-sm text-gray-900">UltraMsg</p>
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-500">Telefono WhatsApp</label>
            <p className="mt-1 text-sm text-gray-900">+507 6000-0000</p>
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-500">Horario de atencion</label>
            <p className="mt-1 text-sm text-gray-900">08:00 - 17:00</p>
          </div>
          <div>
            <label className="block text-xs font-medium text-gray-500">Zona horaria</label>
            <p className="mt-1 text-sm text-gray-900">America/Panama</p>
          </div>
        </div>
        <p className="text-xs text-gray-400">
          La configuracion del tenant se administra desde el backend. Contacta al administrador para cambios.
        </p>
      </div>
    </div>
  )
}
