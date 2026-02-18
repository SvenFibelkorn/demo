import { createRouter, createWebHistory } from 'vue-router'
import DaySummaryView from '@/views/DaySummaryView.vue'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'day-summary',
      component: DaySummaryView,
    },
    {
      path: '/search',
      name: 'search',
      component: () => import('@/views/SearchView.vue'),
    },
    {
      path: '/newest',
      name: 'newest',
      component: () => import('@/views/NewestView.vue'),
    },
  ],
})

export default router
