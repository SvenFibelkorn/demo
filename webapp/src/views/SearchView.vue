<script setup lang="ts">
import { ref } from 'vue'

import ArticleList from '@/components/ArticleList.vue'
import { searchArticlesSemantic, searchArticlesText } from '@/lib/api'
import { ORGANIZATION_FILTERS, organizationNameFromSlug } from '@/lib/organizations'
import type { Article } from '@/types/news'

const query = ref('')
const organizationSlug = ref('')
const searchMode = ref<'text' | 'semantic'>('text')
const loading = ref(false)
const errorMessage = ref('')
const articles = ref<Article[]>([])

async function runSearch(): Promise<void> {
  const searchTerm = query.value.trim()
  if (!searchTerm) {
    errorMessage.value = 'Please enter a search string.'
    articles.value = []
    return
  }

  loading.value = true
  errorMessage.value = ''

  try {
    if (searchMode.value === 'text') {
      articles.value = await searchArticlesText(searchTerm, organizationSlug.value || undefined)
    } else {
      const name = organizationSlug.value ? organizationNameFromSlug(organizationSlug.value) : undefined
      articles.value = await searchArticlesSemantic(searchTerm, name)
    }
  } catch (error) {
    articles.value = []
    errorMessage.value = error instanceof Error ? error.message : 'Search failed.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <section class="panel">
    <h1>Article Search</h1>
    <p class="lead">Search by text or semantic similarity. Text search is the default mode.</p>

    <form class="form" @submit.prevent="runSearch">
      <label for="search-query">Search text</label>
      <input id="search-query" v-model="query" type="text" placeholder="Enter keywords or a question" />

      <label for="search-organization">Organization filter (optional)</label>
      <select id="search-organization" v-model="organizationSlug">
        <option value="">All organizations</option>
        <option v-for="option in ORGANIZATION_FILTERS" :key="option.slug" :value="option.slug">
          {{ option.label }}
        </option>
      </select>

      <fieldset class="toggle-group">
        <legend>Search mode</legend>
        <label>
          <input v-model="searchMode" type="radio" value="text" />
          Text
        </label>
        <label>
          <input v-model="searchMode" type="radio" value="semantic" />
          Semantic
        </label>
      </fieldset>

      <button type="submit" class="button primary" :disabled="loading">
        {{ loading ? 'Searching…' : 'Search' }}
      </button>
    </form>

    <p v-if="errorMessage" class="error-message">{{ errorMessage }}</p>

    <ArticleList
      :items="articles"
      title="Search results"
      empty-text="Run a search to display matching articles."
    />
  </section>
</template>
