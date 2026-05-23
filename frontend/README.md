# VisionPaint frontend

Vue 3 + Vite + TypeScript + Tailwind CSS v4 + Pinia + Vue Router.

## Setup

```bash
cd frontend
npm install
npm run dev
```

## Environment

Optional `.env`:

```
VITE_API_URL=https://vision-paint-api.azurewebsites.net/api
```

## Design

UI behavior and routes: [../docs/design/ui-spec.md](../docs/design/ui-spec.md)

## Scripts

- `npm run dev` — local dev server (port 5173)
- `npm run build` — production build to `dist/`
- `npm run preview` — preview production build
- `npm run test:e2e` — Playwright browser tests (requires API + dev server; see `.github/workflows/e2e-ui.yml`)

Deployed via Firebase Hosting (`firebase.json`).
