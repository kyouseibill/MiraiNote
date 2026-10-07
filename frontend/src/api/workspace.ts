import { http, unwrap } from './auth'

export interface ChatLibraryFile {
  kind: 'upload' | 'generated' | 'export' | string
  name: string
  relativePath: string
  sizeBytes: number
  extension: string
  modifiedAt: string
  downloadUrl?: string | null
}

export interface ChatFileLibrary {
  organizedCount: number
  organizedPaths: string[]
  uploads: ChatLibraryFile[]
  generated: ChatLibraryFile[]
  exports: ChatLibraryFile[]
  uploadsTruncated: boolean
  generatedTruncated: boolean
  exportsTruncated: boolean
}

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

  library: () => unwrap<ChatFileLibrary>(http.get('/workspace/library', { timeout: 60_000 })),

  download: (path: string) =>
    http.get<Blob>('/workspace/download', {
      params: { path },
      responseType: 'blob',
      timeout: 60_000,
    }),
}
