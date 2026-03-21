import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { X, Loader2 } from 'lucide-react'
import {
  useCreateTenant,
  useUpdateTenant,
  type AdminTenant,
} from '@/modules/admin/hooks/useAdminTenants'

const tenantSchema = z.object({
  name: z.string().min(1, 'El nombre es requerido'),
  slug: z.string().min(1, 'El slug es requerido').regex(/^[a-z0-9-]+$/, 'Solo letras minusculas, numeros y guiones'),
  country: z.string().min(1, 'El pais es requerido'),
  monthlyBillingAmount: z.coerce.number().min(0, 'El monto debe ser mayor o igual a 0'),
})

type TenantForm = z.infer<typeof tenantSchema>

interface TenantFormModalProps {
  tenant?: AdminTenant
  onClose: () => void
}

function slugify(text: string): string {
  return text
    .toLowerCase()
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

export function TenantFormModal({ tenant, onClose }: TenantFormModalProps) {
  const isEdit = !!tenant
  const createTenant = useCreateTenant()
  const updateTenant = useUpdateTenant()
  const [error, setError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    setValue,
    formState: { errors },
  } = useForm<TenantForm>({
    resolver: zodResolver(tenantSchema),
    defaultValues: isEdit
      ? {
          name: tenant.name,
          slug: tenant.slug,
          country: tenant.country,
          monthlyBillingAmount: tenant.monthlyBillingAmount,
        }
      : {
          name: '',
          slug: '',
          country: 'Panama',
          monthlyBillingAmount: 0,
        },
  })

  const handleNameChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const name = e.target.value
    if (!isEdit) {
      setValue('slug', slugify(name))
    }
  }

  const onSubmit = async (data: TenantForm) => {
    setError(null)
    try {
      if (isEdit) {
        await updateTenant.mutateAsync({
          id: tenant.id,
          name: data.name,
          country: data.country,
          monthlyBillingAmount: data.monthlyBillingAmount,
        })
      } else {
        await createTenant.mutateAsync({
          name: data.name,
          slug: data.slug,
          country: data.country,
          monthlyBillingAmount: data.monthlyBillingAmount,
        })
      }
      onClose()
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { error?: string } } }
      setError(axiosErr.response?.data?.error ?? 'Error al guardar. Intenta nuevamente.')
    }
  }

  const isPending = createTenant.isPending || updateTenant.isPending

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
      <div className="w-full max-w-lg rounded-lg bg-white shadow-xl">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-gray-200 px-6 py-4">
          <h2 className="text-lg font-semibold text-gray-900">
            {isEdit ? 'Editar Cliente' : 'Nuevo Cliente'}
          </h2>
          <button onClick={onClose} className="text-gray-400 hover:text-gray-600">
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Body */}
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 px-6 py-4">
          {error && (
            <div className="rounded-md bg-red-50 p-3 text-sm text-red-600">{error}</div>
          )}

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">Nombre</label>
              <input
                {...register('name', { onChange: handleNameChange })}
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              />
              {errors.name && (
                <p className="mt-1 text-xs text-red-600">{errors.name.message}</p>
              )}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Slug</label>
              <input
                {...register('slug')}
                disabled={isEdit}
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500 disabled:bg-gray-100 disabled:text-gray-500"
              />
              {errors.slug && (
                <p className="mt-1 text-xs text-red-600">{errors.slug.message}</p>
              )}
            </div>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700">Pais</label>
              <input
                {...register('country')}
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              />
              {errors.country && (
                <p className="mt-1 text-xs text-red-600">{errors.country.message}</p>
              )}
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Monto Mensual</label>
              <input
                type="number"
                step="0.01"
                {...register('monthlyBillingAmount')}
                className="mt-1 block w-full rounded-md border border-gray-300 px-3 py-2 text-sm focus:border-indigo-500 focus:outline-none focus:ring-1 focus:ring-indigo-500"
              />
              {errors.monthlyBillingAmount && (
                <p className="mt-1 text-xs text-red-600">
                  {errors.monthlyBillingAmount.message}
                </p>
              )}
            </div>
          </div>

          {/* Footer */}
          <div className="flex justify-end gap-3 border-t border-gray-200 pt-4">
            <button
              type="button"
              onClick={onClose}
              className="rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={isPending}
              className="flex items-center gap-2 rounded-md bg-indigo-600 px-4 py-2 text-sm font-medium text-white hover:bg-indigo-700 disabled:opacity-50"
            >
              {isPending && <Loader2 className="h-4 w-4 animate-spin" />}
              {isEdit ? 'Guardar cambios' : 'Crear cliente'}
            </button>
          </div>
        </form>
      </div>
    </div>
  )
}
