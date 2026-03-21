import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

// --- Types ---

export interface AdminTenant {
  id: string
  name: string
  slug: string
  country: string
  monthlyBillingAmount: number
  isActive: boolean
  createdAt: string
  whatsAppProvider: string
  whatsAppPhoneNumber: string
  userCount: number
}

export interface CreateTenantPayload {
  name: string
  slug: string
  country: string
  monthlyBillingAmount: number
}

export interface UpdateTenantPayload {
  name?: string
  country?: string
  monthlyBillingAmount?: number
  isActive?: boolean
}

export interface AdminTenantUser {
  id: string
  fullName: string
  email: string
  role: string
  isActive: boolean
  createdAt: string
  lastLoginAt: string | null
}

export interface CreateTenantUserPayload {
  fullName: string
  email: string
  password: string
  role: string
}

export interface ChangePasswordPayload {
  tenantId: string
  userId: string
  newPassword: string
}

// --- Hooks ---

export function useAdminTenants() {
  return useQuery({
    queryKey: ['admin', 'tenants'],
    queryFn: async () => {
      const { data } = await adminClient.get<AdminTenant[]>('/admin/tenants')
      return data
    },
  })
}

export function useCreateTenant() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (payload: CreateTenantPayload) => {
      const { data } = await adminClient.post('/admin/tenants', payload)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['admin', 'tenants'] })
    },
  })
}

export function useUpdateTenant() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, ...payload }: UpdateTenantPayload & { id: string }) => {
      const { data } = await adminClient.put(`/admin/tenants/${id}`, payload)
      return data
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['admin', 'tenants'] })
    },
  })
}

export function useAdminTenantUsers(tenantId: string | null) {
  return useQuery({
    queryKey: ['admin', 'tenants', tenantId, 'users'],
    queryFn: async () => {
      const { data } = await adminClient.get<AdminTenantUser[]>(
        `/admin/tenants/${tenantId}/users`
      )
      return data
    },
    enabled: !!tenantId,
  })
}

export function useCreateTenantUser() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async ({
      tenantId,
      ...payload
    }: CreateTenantUserPayload & { tenantId: string }) => {
      const { data } = await adminClient.post(
        `/admin/tenants/${tenantId}/users`,
        payload
      )
      return data
    },
    onSuccess: (_data, variables) => {
      queryClient.invalidateQueries({
        queryKey: ['admin', 'tenants', variables.tenantId, 'users'],
      })
      queryClient.invalidateQueries({ queryKey: ['admin', 'tenants'] })
    },
  })
}

export function useChangeTenantUserPassword() {
  return useMutation({
    mutationFn: async ({ tenantId, userId, newPassword }: ChangePasswordPayload) => {
      const { data } = await adminClient.put(
        `/admin/tenants/${tenantId}/users/${userId}/password`,
        { newPassword }
      )
      return data
    },
  })
}
