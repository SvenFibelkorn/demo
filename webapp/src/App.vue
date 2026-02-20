<script setup lang="ts">
import { RouterLink, RouterView } from 'vue-router'
</script>

<template>
  <div class="app-shell">
    <header class="topbar">
      <div class="brand">
        <p class="kicker">Sven Fibelkorn's Demo</p>
        <h1>News Desk</h1>
      </div>
      <nav>
        <RouterLink to="/">Day Summary</RouterLink>
        <RouterLink to="/search">Search</RouterLink>
        <RouterLink to="/newest">Newest</RouterLink>
      </nav>
    </header>

    <main class="main-content">
      <RouterView />
    </main>
  </div>
</template>

<style scoped>
.app-shell {
  min-height: 100vh;
}

.topbar {
  display: flex;
  align-items: flex-end;
  justify-content: space-between;
  gap: 1rem;
  border-bottom: 1px solid var(--color-border);
  padding-bottom: 1.25rem;
  margin-bottom: 1.25rem;
}

.brand h1 {
  font-size: clamp(1.35rem, 1.2rem + 0.85vw, 1.9rem);
  font-weight: 700;
  letter-spacing: 0.01em;
}

.kicker {
  text-transform: uppercase;
  letter-spacing: 0.08em;
  font-size: 0.7rem;
  font-weight: 600;
  color: var(--color-text-muted);
}

nav {
  display: flex;
  gap: 0.25rem;
  flex-wrap: wrap;
}

nav a.router-link-exact-active {
  color: var(--color-on-accent);
  background: var(--color-accent);
  border-color: transparent;
}

nav a.router-link-exact-active:hover {
  background: var(--color-accent);
}

nav a {
  border: 1px solid var(--color-border);
  border-radius: 999px;
  color: var(--color-text);
  font-size: 0.9rem;
  font-weight: 500;
  padding: 0.35rem 0.8rem;
  transition: background-color 0.2s ease;
}

nav a:hover {
  background: var(--color-background-soft);
}

.main-content {
  padding-bottom: 2rem;
}

@media (max-width: 760px) {
  .topbar {
    flex-direction: column;
    align-items: flex-start;
  }
}
</style>
