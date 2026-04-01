import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'

interface Profile {
  id: string
  fullName: string
  email: string
  role: string
  avatarUrl?: string | null
  createdAt?: string | null
}

export function useProfile() {
  return useQuery({
    queryKey: ['profile'],
    queryFn: () => api.get<Profile>('/profile').then((r) => r.data),
  })
}

export function useUpdateProfile() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (data: { fullName: string }) =>
      api.put<Profile>('/profile', data).then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['profile'] })
    },
  })
}

export function useUploadAvatar() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (file: File) => {
      const formData = new FormData()
      formData.append('photo', file)
      return api
        .post<{ avatarUrl: string }>('/profile/avatar', formData)
        .then((r) => r.data)
    },
    onSuccess: (data) => {
      // Actualizar el cache directamente con la nueva data URL
      // para que el avatar aparezca de inmediato sin esperar el refetch
      qc.setQueryData<Profile>(['profile'], (prev) =>
        prev ? { ...prev, avatarUrl: data.avatarUrl } : prev
      )
      qc.invalidateQueries({ queryKey: ['profile'] })
    },
  })
}

export function useDeleteAvatar() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: () => api.delete('/profile/avatar').then((r) => r.data),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['profile'] })
    },
  })
}

export function useChangePassword() {
  return useMutation({
    mutationFn: (data: { currentPassword: string; newPassword: string }) =>
      api.post('/profile/change-password', data).then(r => r.data),
  })
}
