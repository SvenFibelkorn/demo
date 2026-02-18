<script setup lang="ts">
import { onMounted, ref } from 'vue'

import ArticleList from '@/components/ArticleList.vue'
import { getNewestArticles } from '@/lib/api'
import { ORGANIZATION_FILTERS } from '@/lib/organizations'
import type { Article } from '@/types/news'

const selectedOrganization = ref('')
const loading = ref(false)
const errorMessage = ref('')
const articles = ref<Article[]>([])

async function loadNewest(): Promise<void> {
  loading.value = true
  errorMessage.value = ''

  try {
    articles.value = await getNewestArticles(selectedOrganization.value || undefined)
  } catch (error) {
    articles.value = []
    errorMessage.value = error instanceof Error ? error.message : 'Failed to load newest articles.'
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  await loadNewest()
})
</script>

<template>
  <section class="panel">
    <h1>Newest Articles</h1>
    <p class="lead">Display the 10 newest articles with an optional organization filter.</p>

    <form class="form inline" @submit.prevent="loadNewest">
      <label for="newest-organization">Organization (optional)</label>
      <select id="newest-organization" v-model="selectedOrganization">
        <option value="">All organizations</option>
        <option v-for="option in ORGANIZATION_FILTERS" :key="option.slug" :value="option.name">
          {{ option.label }}
        </option>
      </select>

      <button type="submit" class="button primary" :disabled="loading">
        {{ loading ? 'Loading…' : 'Refresh' }}
      </button>
    </form>

    <p v-if="errorMessage" class="error-message">{{ errorMessage }}</p>

    <ArticleList
      :items="articles"
      title="Newest results"
      empty-text="No articles found for this filter."
    />
  </section>
</template>
