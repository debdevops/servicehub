/** The id an address names, or null: `?id=` must be whole digits, so nothing else is ever asked of the API. */
export const messageIdOf = (raw: string | null): number | null => (raw !== null && /^\d+$/.test(raw) ? Number(raw) : null)
