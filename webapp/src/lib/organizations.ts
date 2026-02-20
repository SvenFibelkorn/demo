export interface OrganizationFilterOption {
  label: string
  slug: string
  name: string
}

export const ORGANIZATION_FILTERS: OrganizationFilterOption[] = [
  {
    label: 'The Economist',
    slug: 'the-economist',
    name: 'The Economist',
  },
  {
    label: 'The Verge',
    slug: 'the-verge',
    name: 'The Verge',
  },
  {
    label: 'Die Zeit',
    slug: 'die-zeit',
    name: 'Die Zeit',
  },
  {
    label: 'Foreign Affairs',
    slug: 'foreign-affairs',
    name: 'Foreign Affairs',
  },
  {
    label: 'Semafor',
    slug: 'semafor',
    name: 'Semafor',
  },
  {
    label: 'Ars Technica',
    slug: 'ars-technica',
    name: 'Ars Technica',
  },
  {
    label: 'FAZ',
    slug: 'faz',
    name: 'FAZ',
  },
  {
    label: 'Deutsche Welle',
    slug: 'dw',
    name: 'Deutsche Welle',
  },
  
]

const slugToNameMap = new Map(ORGANIZATION_FILTERS.map((item) => [item.slug, item.name]))

export function organizationNameFromSlug(slug: string): string {
  const normalized = slug.trim().toLowerCase()
  return slugToNameMap.get(normalized) ?? slug
}
