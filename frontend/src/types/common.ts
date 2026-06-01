// 通用分页结果（与后端 PagedResult<T> 对齐）
export interface PagedResult<T> {
  page: number
  pageSize: number
  total: number
  items: T[]
}
