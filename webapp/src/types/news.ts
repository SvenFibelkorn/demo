export interface Organization {
  id: string
  url: string
  name: string
}

export interface Article {
  id: string
  link: string
  organizationId: string
  organization?: Organization | null
  headline?: string | null
  description?: string | null
  summary?: string | null
  content?: string | null
  publicationDate?: string | null
}

export interface DaySummaryResponse {
  query: string
  organizationSlug?: string | null
  cutoffUtc: string
  articleCount: number
  summary?: string | null
}

export interface ApiErrorBody {
  error?: string
  detail?: string
  title?: string
}
