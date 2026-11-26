/**
 * Turning a question into a search. Kept apart from the database so it can be tested on its own.
 *
 * A question becomes "groups": one group per meaningful word, each group holding that word and its synonyms. A group is
 * matched when any of its alternatives is found. Coverage (how many groups an entry matches) decides whether the
 * assistant answers or only offers suggestions.
 */

const STOPWORDS = new Set([
  'a', 'about', 'all', 'an', 'and', 'any', 'are', 'as', 'at', 'be', 'by', 'can', 'could', 'did', 'do', 'does', 'for',
  'from', 'get', 'go', 'has', 'have', 'help', 'how', 'i', 'if', 'in', 'is', 'it', 'its', 'me', 'my', 'of', 'on', 'or',
  'our', 'please', 'should', 'so', 'some', 'tell', 'that', 'the', 'there', 'these', 'this', 'those', 'to', 'up', 'us',
  'want', 'was', 'we', 'what', 'when', 'where', 'which', 'who', 'why', 'will', 'with', 'would', 'you', 'your',
])

/** Most questions are a sentence. A pasted log line is not a question, so only the first words count. */
const MAX_TERMS = 12

/** The meaningful words of a question, lowercased, in order, without repeats. */
export function terms(question: string): string[] {
  const words = question.toLowerCase().normalize('NFKC').match(/[\p{L}\p{N}]+/gu) ?? []
  const seen = new Set<string>()
  for (const word of words) {
    if (STOPWORDS.has(word) || (word.length < 2 && !/\d/.test(word))) continue
    seen.add(word)
    if (seen.size === MAX_TERMS) break
  }
  return [...seen]
}

/** One group per term: the term itself, then its synonyms (each may be a phrase such as `dead letter`). */
export function groups(question: string, synonyms: ReadonlyMap<string, readonly string[]>): string[][] {
  return terms(question).map((term) => [term, ...(synonyms.get(term) ?? [])])
}

/**
 * A rough stem, only for comparing a question with a stored question: "letters", "lettered" → "letter". A plain `-s` is
 * dropped, never `-es`, so "messages" and "message" meet; `-ss` ("access") is kept.
 */
export function stem(word: string): string {
  for (const suffix of ['ing', 'ed', 's']) {
    if (word.length > suffix.length + 3 && word.endsWith(suffix) && !word.endsWith('ss')) return word.slice(0, -suffix.length)
  }
  return word
}

/**
 * How closely a question matches one stored question or heading, 0..1: matched question groups over everything either
 * side says (a Jaccard index). A group is matched when any alternative is in the line; a phrase alternative needs all its
 * words, and then covers all of them ("dlq" fully matches "Dead letters"). This is what makes "Did my replay work?" beat an
 * entry that merely says "replay" ten times.
 */
export function similarity(questionGroups: readonly (readonly string[])[], line: string): number {
  const lineStems = new Set(terms(line).map(stem))
  if (questionGroups.length === 0 || lineStems.size === 0) return 0
  let matched = 0
  const covered = new Set<string>()
  for (const alternatives of questionGroups) {
    const hit = alternatives.map((alt) => alt.split(/\s+/).map(stem)).find((words) => words.every((w) => lineStems.has(w)))
    if (hit) {
      matched += 1
      for (const w of hit) covered.add(w)
    }
  }
  return matched / (questionGroups.length + lineStems.size - covered.size)
}

/**
 * FTS5 query text for a set of alternatives. Every alternative is double-quoted, so nothing a person types can be read as
 * FTS5 syntax (`NEAR`, `*`, `-`, column filters): user text is always data.
 */
export function ftsAny(alternatives: readonly string[]): string {
  return alternatives.map((a) => `"${a.replaceAll('"', '""')}"`).join(' OR ')
}
