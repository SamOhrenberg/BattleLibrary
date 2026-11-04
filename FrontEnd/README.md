# The Battle Library: frontend

React 19 + TypeScript app built with Vite. See the
[root README](../README.md#running-locally) for full local setup, including
the API it talks to.

## Commands

```bash
npm install
npm run dev       # dev server at http://localhost:5173
npm run lint      # oxlint
npm run build     # type-check (tsc -b) and production build to dist/
npm run preview   # serve the production build locally
```

## Configuration

Build-time variables (see `.env.example`; `.env.development` is used by
`npm run dev`):

- `VITE_API_BASE_URL`: base URL of the API, no trailing slash
- `VITE_BASE`: base path for the build, `/BattleLibrary/` on GitHub Pages

## Layout

`src/` is grouped by feature (`auth`, `lists`, `games`, `social`,
`onboarding`), with API clients and shared helpers in `lib/`, shared
components in `ui/`, and theme styles in `styles/`.

The UI is mobile first: style for a phone, then enhance with `min-width`
media queries. Use the theme tokens rather than fixed colors so every theme
keeps working.
