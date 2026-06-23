import { http, unwrap } from './auth'

export interface WorkspaceEntry {
  name: string
  relativePath: string
  type: 'file' | 'dir'
  sizeBytes: number
  extension: string
}

export interface WorkspaceDirResult {
  scope: string
  currentPath: string
  entries: WorkspaceEntry[]
}

export interface WorkspaceAttachResult {
  fileName: string
  fileType: string
  textContent: string
  fileSizeBytes: number
  relativePath: string
  scope: string
}

export const workspaceApi = {
  browse: (scope: 'private' | 'public' = 'private', path?: string) =>
    unwrap<WorkspaceDirResult>(
      http.get('/workspace/files', { params: { scope, path: path || undefined } }),
    ),

  attach: (path: string, scope: 'private' | 'public' = 'private') =>
    unwrap<WorkspaceAttachResult>(http.post('/workspace/attach', { path, scope })),
}
