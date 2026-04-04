import { describe, it, expect, vi, beforeEach } from 'vitest';
import { copyToClipboard } from '@/lib/clipboard';

describe('copyToClipboard', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('uses navigator.clipboard.writeText when available and context is secure', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText },
      writable: true,
      configurable: true,
    });
    Object.defineProperty(window, 'isSecureContext', {
      value: true,
      writable: true,
      configurable: true,
    });

    const result = await copyToClipboard('hello world');
    expect(writeText).toHaveBeenCalledWith('hello world');
    expect(result).toBe(true);
  });

  it('returns false when navigator.clipboard.writeText rejects', async () => {
    const writeText = vi.fn().mockRejectedValue(new Error('no permission'));
    Object.defineProperty(navigator, 'clipboard', {
      value: { writeText },
      writable: true,
      configurable: true,
    });
    Object.defineProperty(window, 'isSecureContext', {
      value: true,
      writable: true,
      configurable: true,
    });

    const result = await copyToClipboard('test');
    expect(result).toBe(false);
  });

  it('falls back to execCommand when not in secure context', async () => {
    Object.defineProperty(window, 'isSecureContext', {
      value: false,
      writable: true,
      configurable: true,
    });
    // execCommand is not implemented in jsdom; override to return true
    const execCommand = vi.fn().mockReturnValue(true);
    document.execCommand = execCommand;

    const result = await copyToClipboard('fallback text');
    expect(execCommand).toHaveBeenCalledWith('copy');
    expect(result).toBe(true);
  });

  it('falls back to execCommand when navigator.clipboard is undefined', async () => {
    Object.defineProperty(navigator, 'clipboard', {
      value: undefined,
      writable: true,
      configurable: true,
    });
    Object.defineProperty(window, 'isSecureContext', {
      value: true,
      writable: true,
      configurable: true,
    });
    const execCommand = vi.fn().mockReturnValue(true);
    document.execCommand = execCommand;

    const result = await copyToClipboard('no-clipboard text');
    expect(execCommand).toHaveBeenCalledWith('copy');
    expect(result).toBe(true);
  });

  it('returns false when execCommand returns false', async () => {
    Object.defineProperty(navigator, 'clipboard', {
      value: undefined,
      writable: true,
      configurable: true,
    });
    Object.defineProperty(window, 'isSecureContext', {
      value: false,
      writable: true,
      configurable: true,
    });
    document.execCommand = vi.fn().mockReturnValue(false);

    const result = await copyToClipboard('will fail');
    expect(result).toBe(false);
  });
});
