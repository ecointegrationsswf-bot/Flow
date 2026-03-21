import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { adminClient } from '@/shared/api/adminClient'

export interface AdminUser {
  id: string
  fullName: string
  email: string
  isActive: boolean
  createdAt: string
  lastLoginAt: string | null
}

export function useAdminUsers() {
  return useQuery({
    queryKey: ['admin', 'users'],
    queryFn: async () => {
      const { data } = await adminClient.get<AdminUser[]>('/admin/users')
      return data
    },
  })
}

export function useCreateAdminUser() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (payload: { fullName: string; email: string; password: string }) => {
      const { data } = await adminClient.post('/admin/users', payload)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'users'] }),
  })
}

export function useUpdateAdminUser() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, ...payload }: { id: string; fullName?: string; email?: string; isActive?: boolean; password?: string }) => {
      const { data } = await adminClient.put(`/admin/users/${id}`, payload)
      return data
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin', 'users'] }),
  })
}
