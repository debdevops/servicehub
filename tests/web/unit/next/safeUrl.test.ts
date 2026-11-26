import { describe, expect, it } from 'vitest'
import { httpsUrl } from '@/next/lib/safeUrl'

describe('httpsUrl: a link the server sent is only drawn when it is plain https', () => {
  it('keeps an https address', () => {
    expect(httpsUrl('https://portal.azure.com/#@/resource/subscriptions/x')).toBe('https://portal.azure.com/#@/resource/subscriptions/x')
  })

  it.each(['javascript:alert(1)', 'data:text/html,<script>alert(1)</script>', 'http://example.com/', '/relative/path', 'not a url', '', null, undefined])(
    'refuses %s', value => {
      expect(httpsUrl(value)).toBeNull()
    })
})
