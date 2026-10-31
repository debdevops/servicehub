import { describe, expect, it } from 'vitest'
import { redriveWords } from '@/next/lib/redriveWords'

describe('the words of moving a failed run on', () => {
  it('describes a Step Functions redrive as restarting the same run, and never says every step runs again', () => {
    const w = redriveWords('step-functions')
    const text = [...w.will('AWS'), ...w.cannot('AWS')].join(' ')
    expect(w.verb).toBe('redrive')
    expect(w.title).toBe('Redrive a run')
    expect(w.link).toBe('Redrive…')
    expect(text).toContain('restart this one run from the step that failed')
    expect(text).not.toContain('Every step runs again')
    expect(w.button('AWS')).toBe('Ask AWS to redrive')
  })

  it('describes a Logic Apps resubmit as a new run where every step runs again and the original stays failed', () => {
    const w = redriveWords('logic-apps')
    const will = w.will('Azure').join(' ')
    const cannot = w.cannot('Azure').join(' ')
    expect(w.verb).toBe('resubmit')
    expect(w.title).toBe('Resubmit a run')
    expect(w.link).toBe('Resubmit…')
    expect(will).toContain('starts a new run')
    expect(will).toContain('Every step runs again, including steps that already succeeded')
    expect(cannot).toContain('It stays failed')
    expect(cannot).toContain('Steps that send, write or charge outside the cloud will do it again')
    expect(cannot).toContain('ServiceHub refuses, because Azure would run a resubmit on a disabled Logic App anyway')
    expect(cannot).not.toContain('Logic App may be disabled')
    expect(`${will} ${cannot}`).not.toMatch(/restart this one run|from the step that failed/)
    expect(w.button('Azure')).toBe('Ask Azure to resubmit')
  })

  it('always says the record will be Unverified, whichever action it is', () => {
    for (const key of ['step-functions', 'logic-apps', 'something-new', null, undefined]) {
      expect(redriveWords(key).cannot('The cloud').join(' ')).toContain('Unverified')
    }
  })

  it('gives a component with no words of its own the redrive words, so a new component never shows blank text', () => {
    expect(redriveWords('something-new')).toBe(redriveWords('step-functions'))
    expect(redriveWords(null)).toBe(redriveWords('step-functions'))
  })
})
