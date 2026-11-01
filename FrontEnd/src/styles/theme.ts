// Theme palettes and the (per-browser) selected-theme state. A theme is a
// complete look chosen explicitly; the active one is set via data-palette on
// <html>. Holo-Vox is the default.

export interface ThemeDef {
  id: string
  name: string
  // Signature accent, for the picker swatch.
  swatch: string
  blurb: string
}

export const THEMES: ThemeDef[] = [
  { id: 'holo', name: 'Holo-Vox', swatch: '#56e6ff', blurb: 'Holographic duotone, glass panels' },
  { id: 'cogitator', name: 'Cogitator', swatch: '#2fd6d6', blurb: 'Industrial slate, cyan signal' },
  { id: 'auspex', name: 'Auspex', swatch: '#e2611f', blurb: 'Light tactical, signal orange' },
  { id: 'phosphor', name: 'Phosphor', swatch: '#58f08a', blurb: 'Terminal green on black' },
  { id: 'ceramite', name: 'Ceramite', swatch: '#f2a516', blurb: 'Brutalist charcoal, amber slab' },
]

export const DEFAULT_THEME = 'holo'

const KEY = 'bl-theme'

export function getTheme(): string {
  try {
    const saved = localStorage.getItem(KEY)
    if (saved && THEMES.some((t) => t.id === saved)) {
      return saved
    }
  } catch {
    // Storage blocked/unavailable; fall back to the default.
  }
  return DEFAULT_THEME
}

export function applyTheme(id: string): void {
  document.documentElement.dataset.palette = id
  try {
    localStorage.setItem(KEY, id)
  } catch {
    // Non-fatal; the theme still applies for this session.
  }
}

// Set the saved (or default) theme before first paint.
export function initTheme(): void {
  document.documentElement.dataset.palette = getTheme()
}
