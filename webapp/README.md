# News Desk Webapp

Vue 3 frontend for the .NET news endpoints in `../dotnet`.

## Features

- Day summary page (`/`): asks questions against `/api/articles/day-summary` with optional organization filters (`The Economist`, `The Verge`, `Die Zeit`).
- Search page (`/search`): text search (`/api/articles/search`) or semantic search (`/api/articles/similar/text`) with optional organization slug input.
- Newest page (`/newest`): latest 10 articles from `/api/articles/newest` with optional organization filter.
- Reusable article list actions:
  - `🌐 Archive` opens `archive.is` for the article link.
  - `Find similar` clears the current list and loads `/api/articles/similar` results, with the clicked article pinned at the top.

## Backend URL configuration

Set the API host with:

```sh
VITE_API_BASE_URL=http://localhost:5271
```

If not set, the frontend defaults to `http://localhost:5271`.

## Docker

Build the production image (set API URL at build time):

```sh
docker build \
  --build-arg VITE_API_BASE_URL=http://localhost:5271 \
  -t news-desk-webapp \
  .
```

Run locally:

```sh
docker run --rm -p 8080:80 news-desk-webapp
```

Then open `http://localhost:8080`.

### Kubernetes note

Because this is a static Vite build served by nginx, `VITE_API_BASE_URL` is compiled into the bundle during image build. For different environments (dev/stage/prod), build separate images or add a runtime config injection step.

## Recommended IDE Setup

[VS Code](https://code.visualstudio.com/) + [Vue (Official)](https://marketplace.visualstudio.com/items?itemName=Vue.volar) (and disable Vetur).

## Recommended Browser Setup

- Chromium-based browsers (Chrome, Edge, Brave, etc.):
  - [Vue.js devtools](https://chromewebstore.google.com/detail/vuejs-devtools/nhdogjmejiglipccpnnnanhbledajbpd)
  - [Turn on Custom Object Formatter in Chrome DevTools](http://bit.ly/object-formatters)
- Firefox:
  - [Vue.js devtools](https://addons.mozilla.org/en-US/firefox/addon/vue-js-devtools/)
  - [Turn on Custom Object Formatter in Firefox DevTools](https://fxdx.dev/firefox-devtools-custom-object-formatters/)

## Type Support for `.vue` Imports in TS

TypeScript cannot handle type information for `.vue` imports by default, so we replace the `tsc` CLI with `vue-tsc` for type checking. In editors, we need [Volar](https://marketplace.visualstudio.com/items?itemName=Vue.volar) to make the TypeScript language service aware of `.vue` types.

## Customize configuration

See [Vite Configuration Reference](https://vite.dev/config/).

## Project Setup

```sh
npm install
```

### Compile and Hot-Reload for Development

```sh
npm run dev
```

### Type-Check, Compile and Minify for Production

```sh
npm run build
```

### Run Unit Tests with [Vitest](https://vitest.dev/)

```sh
npm run test:unit
```

### Run End-to-End Tests with [Playwright](https://playwright.dev)

```sh
# Install browsers for the first run
npx playwright install

# When testing on CI, must build the project first
npm run build

# Runs the end-to-end tests
npm run test:e2e
# Runs the tests only on Chromium
npm run test:e2e -- --project=chromium
# Runs the tests of a specific file
npm run test:e2e -- tests/example.spec.ts
# Runs the tests in debug mode
npm run test:e2e -- --debug
```

### Lint with [ESLint](https://eslint.org/)

```sh
npm run lint
```
