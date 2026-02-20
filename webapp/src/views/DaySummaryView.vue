<script setup lang="ts">
import { ref } from 'vue'

import { getDaySummary } from '@/lib/api'
import { ORGANIZATION_FILTERS } from '@/lib/organizations'
import type { DaySummaryResponse } from '@/types/news'

const question = ref('')
const organizationSlug = ref('')
const loading = ref(false)
const errorMessage = ref('')
const result = ref<DaySummaryResponse | null>(null)

async function submitSummaryRequest(): Promise<void> {
  if (!question.value.trim()) {
    errorMessage.value = 'Please enter a question first.'
    result.value = null
    return
  }

  loading.value = true
  errorMessage.value = ''

  try {
    result.value = await getDaySummary(question.value, organizationSlug.value || undefined)
  } catch (error) {
    result.value = null
    errorMessage.value = error instanceof Error ? error.message : 'Failed to generate day summary.'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <section class="panel">
    <h1>Daily News Assistant</h1>
    <p class="lead">
      Ask questions about the newest articles from the last 24 hours.
    </p>

    <form class="form" @submit.prevent="submitSummaryRequest">
      <label for="question">Question</label>
      <textarea
        id="question"
        v-model="question"
        rows="4"
        placeholder="What happened in AI regulation today?"
      />

      <label for="organization">News organization</label>
      <select id="organization" v-model="organizationSlug">
        <option value="">All organizations</option>
        <option v-for="option in ORGANIZATION_FILTERS" :key="option.slug" :value="option.slug">
          {{ option.label }}
        </option>
      </select>

      <button type="submit" class="button primary" :disabled="loading">
        {{ loading ? 'Generating…' : 'Generate summary' }}
      </button>
    </form>

    <p v-if="errorMessage" class="error-message">{{ errorMessage }}</p>

    <section v-if="result" class="result-card">
      <h2>Summary</h2>
      <p class="result-meta">
        {{ result.articleCount }} articles analyzed • since {{ new Date(result.cutoffUtc).toLocaleString() }}
      </p>
      <p class="summary-text">{{ result.summary || 'No summary content returned.' }}</p>
    </section>
  </section>
</template>
