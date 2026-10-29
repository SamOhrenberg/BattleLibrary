// Content "tiers" layered on top of a faction's core range. Both are detectable
// from data already served: Legends units carry a "[Legends]" name suffix;
// Crucible units carry "[Crucible]" and/or the "Crucible" keyword.

export const isLegends = (name: string) => name.includes('[Legends]')

export const isCrucible = (name: string, keywords: readonly string[] = []) =>
  name.includes('[Crucible]') || keywords.some((k) => k.toLowerCase() === 'crucible')
