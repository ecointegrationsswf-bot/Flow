import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'
import type { AppUser } from '@/shared/types'

export function useUsers() {
  return useQuery<AppUser[]>({
    queryKey: ['users'],
    queryFn: async () => {
      const { data } = await api.get('/users')
      return data
    },
  })
}

interface CreateUserPayload {
  fullName: string
  email: string
  password: string
  role: string
  canEditPhone: boolean
  allowedAgentIds: string[]
}

export function useCreateUser() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (payload: CreateUserPayload) => {
      const { data } = await api.post('/users', payload)
      return data as AppUser
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['users'] }),
  })
}

interface UpdateUserPayload {
  id: string
  fullName: string
  email: string
  role: string
  isActive: boolean
  canEditPhone: boolean
  allowedAgentIds: string[]
  password?: string
}

export function useUpdateUser() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async ({ id, ...payload }: UpdateUserPayload) => {
      const { data } = await api.put(`/users/${id}`, payload)
      return data as AppUser
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['users'] }),
  })
}

export function useDeleteUser() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: async (id: string) => {
      await api.delete(`/users/${id}`)
    },
    onSuccess: () => qc.invalidateQueries({ queryKey: ['users'] }),
  })
}
