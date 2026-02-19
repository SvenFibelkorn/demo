import type { Article, ApiErrorBody, DaySummaryResponse } from '@/types/news'

const baseUrl = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5271').replace(/\/$/, '')
const apiRoot = `${baseUrl}/api`

const htmlDecodeElement = typeof document !== 'undefined' ? document.createElement('textarea') : null

function decodeHtmlEntities(value?: string | null): string | null | undefined {
  if (value == null || value.length === 0 || !htmlDecodeElement) {
    return value
  }

  htmlDecodeElement.innerHTML = value
  return htmlDecodeElement.value
}

function normalizeArticle(article: Article): Article {
  return {
    ...article,
    headline: decodeHtmlEntities(article.headline),
    description: decodeHtmlEntities(article.description),
    summary: decodeHtmlEntities(article.summary),
    content: decodeHtmlEntities(article.content),
  }
}

function normalizeArticles(articles: Article[]): Article[] {
  return articles.map(normalizeArticle)
}

async function parseError(response: Response): Promise<string> {
  let fallback = `Request failed with status ${response.status}`

  try {
    const body = (await response.json()) as ApiErrorBody
    return body.error || body.detail || body.title || fallback
  } catch {
    return fallback
  }
}

async function requestJson<T>(input: RequestInfo | URL, init?: RequestInit): Promise<T> {
  const response = await fetch(input, init)

  if (!response.ok) {
    const message = await parseError(response)
    throw new Error(message)
  }

  return (await response.json()) as T
}

export async function getNewestArticles(organizationName?: string): Promise<Article[]> {
  const params = new URLSearchParams()

  if (organizationName && organizationName.trim().length > 0) {
    params.set('organization', organizationName.trim())
  }

  const endpoint = params.size > 0 ? `${apiRoot}/articles/newest?${params.toString()}` : `${apiRoot}/articles/newest`
  return normalizeArticles(await requestJson<Article[]>(endpoint))
}

export async function searchArticlesText(query: string, organizationSlug?: string): Promise<Article[]> {
  const params = new URLSearchParams({ query: query.trim() })

  if (organizationSlug && organizationSlug.trim().length > 0) {
    params.set('organizationSlug', organizationSlug.trim())
  }

  return normalizeArticles(await requestJson<Article[]>(`${apiRoot}/articles/search?${params.toString()}`))
}

export async function searchArticlesSemantic(query: string, organizationName?: string): Promise<Article[]> {
  const params = new URLSearchParams()

  if (organizationName && organizationName.trim().length > 0) {
    params.set('organization', organizationName.trim())
  }

  const endpoint = params.size > 0
    ? `${apiRoot}/articles/similar/text?${params.toString()}`
    : `${apiRoot}/articles/similar/text`

  return normalizeArticles(await requestJson<Article[]>(endpoint, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ text: query.trim() }),
  }))
}

export async function getSimilarArticlesFromLink(link: string): Promise<Article[]> {
  const params = new URLSearchParams({ Link: link })
  return normalizeArticles(await requestJson<Article[]>(`${apiRoot}/articles/similar?${params.toString()}`))
}

export async function getDaySummary(query: string, organizationSlug?: string): Promise<DaySummaryResponse> {
  return requestJson<DaySummaryResponse>(`${apiRoot}/articles/day-summary`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      query: query.trim(),
      organizationSlug: organizationSlug && organizationSlug.trim().length > 0 ? organizationSlug.trim() : null,
    }),
  })
}
