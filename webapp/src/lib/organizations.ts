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
]

const slugToNameMap = new Map(ORGANIZATION_FILTERS.map((item) => [item.slug, item.name]))

export function organizationNameFromSlug(slug: string): string {
  const normalized = slug.trim().toLowerCase()
  return slugToNameMap.get(normalized) ?? slug
}
