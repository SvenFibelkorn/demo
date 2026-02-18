<script setup lang="ts">
import { computed, ref, watch } from 'vue'

import { getSimilarArticlesFromLink } from '@/lib/api'
import type { Article } from '@/types/news'

const props = defineProps<{
  items: Article[]
  title?: string
  emptyText?: string
}>()

const displayItems = ref<Article[]>([])
const loadingSimilarId = ref<string | null>(null)
const infoMessage = ref('')
const errorMessage = ref('')

watch(
  () => props.items,
  (nextItems) => {
    displayItems.value = [...nextItems]
    infoMessage.value = ''
    errorMessage.value = ''
  },
  { immediate: true },
)

const hasItems = computed(() => displayItems.value.length > 0)

function headlineFor(article: Article): string {
  if (article.headline && article.headline.trim().length > 0) {
    return article.headline.trim()
  }

  return '(Untitled article)'
}

function teaserFor(article: Article): string {
  return (article.summary || article.description || article.content || '').trim()
}

function publicationFor(article: Article): string {
  if (!article.publicationDate) {
    return 'Unknown publication date'
  }

  const date = new Date(article.publicationDate)
  if (Number.isNaN(date.getTime())) {
    return 'Unknown publication date'
  }

  return date.toLocaleString()
}

function archiveUrl(link: string): string {
  return `https://archive.is/${encodeURIComponent(link)}`
}

async function showSimilar(article: Article): Promise<void> {
  loadingSimilarId.value = article.id
  infoMessage.value = ''
  errorMessage.value = ''

  try {
    const similar = await getSimilarArticlesFromLink(article.link)
    const withoutOriginal = similar.filter((item) => item.id !== article.id)

    displayItems.value = [article, ...withoutOriginal]
    infoMessage.value = 'Showing the selected article and its most similar articles.'
  } catch (error) {
    errorMessage.value = error instanceof Error ? error.message : 'Could not load similar articles.'
  } finally {
    loadingSimilarId.value = null
  }
}
</script>

<template>
  <section class="article-section">
    <div class="section-header">
      <h2 v-if="title">{{ title }}</h2>
      <p v-if="infoMessage" class="info-message">{{ infoMessage }}</p>
      <p v-if="errorMessage" class="error-message">{{ errorMessage }}</p>
    </div>

    <p v-if="!hasItems" class="empty-state">{{ emptyText || 'No articles to display yet.' }}</p>

    <ol v-else class="article-list">
      <li v-for="article in displayItems" :key="article.id" class="article-item">
        <article>
          <header class="article-head">
            <h3>{{ headlineFor(article) }}</h3>
            <p class="meta">
              <span>{{ article.organization?.name || 'Unknown organization' }}</span>
              <span aria-hidden="true">•</span>
              <time>{{ publicationFor(article) }}</time>
            </p>
          </header>

          <p v-if="teaserFor(article)" class="teaser">{{ teaserFor(article) }}</p>

          <div class="actions">
            <a :href="archiveUrl(article.link)" target="_blank" rel="noopener noreferrer" class="button action-link" title="Open archive.is">
              🌐 Archive
            </a>
            <button
              type="button"
              class="button"
              :disabled="loadingSimilarId === article.id"
              @click="showSimilar(article)"
            >
              {{ loadingSimilarId === article.id ? 'Loading…' : 'Find similar' }}
            </button>
          </div>
        </article>
      </li>
    </ol>
  </section>
</template>
