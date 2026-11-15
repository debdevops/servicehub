/**
 * Turns a doc section into the small Markdown the chat bubble shows (paragraphs, lists, code, bold, italic, <https://…>).
 * Code blocks are left exactly as written. Outside them: images and HTML go; `> ` quote markers go (the text stays);
 * `[text](url)` keeps its text (the answer's own "Read more" link goes to the section); a table becomes one list line per
 * row, cells joined by " — ", with its header and separator rows dropped; `####` headings become bold lines.
 */
export function cleanMarkdown(body: string): string {
  const out: string[] = []
  const lines = body.split('\n')
  let inFence = false
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i] ?? ''
    if (/^\s*(```|~~~)/.test(line)) {
      inFence = !inFence
      out.push(line)
      continue
    }
    if (inFence) {
      out.push(line)
      continue
    }
    let text = line.replace(/^\s*>\s?/, '')
    if (/^\s*\|.*\|\s*$/.test(text)) {
      const isSeparator = (l: string) => /^\s*\|[\s:|-]+\|\s*$/.test(l)
      if (isSeparator(text) || isSeparator((lines[i + 1] ?? '').replace(/^\s*>\s?/, ''))) continue
      const cells = text.split('|').map((c) => c.trim()).filter(Boolean)
      text = `- ${cells.join(' — ')}`
    }
    text = text
      .replace(/!\[[^\]]*\]\([^)]*\)/g, '')
      .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
      .replace(/<(?!https?:\/\/)[^>]+>/g, '')
      .replace(/^#{4,6}\s+(.+?)\s*#*\s*$/, '**$1**')
    out.push(text)
  }
  return out.join('\n').replace(/\n{3,}/g, '\n\n').trim()
}
