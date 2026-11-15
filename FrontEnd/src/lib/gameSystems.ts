// The fixed game systems a list or game can be tagged with (mirrors the API).
export const GAME_SYSTEMS = [
  { key: 'wh40k', name: 'Warhammer 40,000' },
  { key: 'kill-team', name: 'Kill Team' },
  { key: 'other', name: 'Other' },
] as const

export type GameSystemKey = (typeof GAME_SYSTEMS)[number]['key']

export function systemName(key: string | null | undefined): string {
  return GAME_SYSTEMS.find((s) => s.key === key)?.name ?? (key || 'Other')
}
