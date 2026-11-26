/**
 * A link the server sent, as an address safe to put in an href: an https:// address, or null. The cloud-console links are built by the
 * server from resource names read from a cloud, so the page does not take their shape on trust — anything that is not plain https
 * (javascript:, data:, a relative path) is not drawn as a link at all.
 */
export function httpsUrl(value: string | null | undefined): string | null {
  if (!value) return null
  try {
    const url = new URL(value)
    return url.protocol === 'https:' ? url.href : null
  } catch {
    return null
  }
}
