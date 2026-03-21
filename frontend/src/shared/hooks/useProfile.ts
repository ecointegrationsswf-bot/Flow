import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '@/shared/api/client'

interface Profile {
  id: string
  fullName: string
  email: string
  role: string
  avatarUrl?: string | null
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
      formData.append('file', file)
      return api
        .post<{ avatarUrl: string }>('/profile/avatar', formData, {
          headers: { 'Content-Type': 'multipart/form-data' },
        })
        .then((r) => r.data)
    },
    onSuccess: () => {
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
