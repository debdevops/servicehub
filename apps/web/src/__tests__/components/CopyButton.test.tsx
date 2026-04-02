import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { CopyButton } from '@/components/CopyButton';

// Mock clipboard lib
vi.mock('@/lib/clipboard', () => ({
  copyToClipboard: vi.fn(),
}));

import { copyToClipboard } from '@/lib/clipboard';
const mockCopy = copyToClipboard as ReturnType<typeof vi.fn>;

describe('CopyButton', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders Copy icon by default', () => {
    mockCopy.mockResolvedValue(true);
    render(<CopyButton text="hello" />);
    const btn = screen.getByRole('button');
    expect(btn).toBeInTheDocument();
    expect(btn).toHaveAttribute('title', 'Copy to clipboard');
  });

  it('shows label text when label prop provided', () => {
    mockCopy.mockResolvedValue(true);
    render(<CopyButton text="hello" label="message ID" />);
    expect(screen.getByText('message ID')).toBeInTheDocument();
    expect(screen.getByRole('button')).toHaveAttribute('aria-label', 'Copy message ID');
  });

  it('calls copyToClipboard with the text when clicked', async () => {
    mockCopy.mockResolvedValue(true);
    render(<CopyButton text="copy-me" />);
    fireEvent.click(screen.getByRole('button'));
    expect(mockCopy).toHaveBeenCalledWith('copy-me');
  });

  it('shows "Copied!" label after successful copy', async () => {
    mockCopy.mockResolvedValue(true);
    render(<CopyButton text="copy-me" label="value" />);
    await act(async () => {
      fireEvent.click(screen.getByRole('button'));
      // flush promise microtasks
      await Promise.resolve();
    });
    expect(screen.getByText('Copied!')).toBeInTheDocument();
  });

  it('resets to original label after 2 seconds', async () => {
    vi.useFakeTimers();
    try {
      mockCopy.mockResolvedValue(true);
      render(<CopyButton text="copy-me" label="value" />);
      await act(async () => {
        fireEvent.click(screen.getByRole('button'));
        await Promise.resolve();
      });
      expect(screen.getByText('Copied!')).toBeInTheDocument();
      act(() => { vi.advanceTimersByTime(2001); });
      expect(screen.getByText('value')).toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });

  it('does not show Copied! when copy fails', async () => {
    mockCopy.mockResolvedValue(false);
    render(<CopyButton text="fail" label="value" />);
    await act(async () => {
      fireEvent.click(screen.getByRole('button'));
      await Promise.resolve();
    });
    expect(screen.queryByText('Copied!')).not.toBeInTheDocument();
    expect(screen.getByText('value')).toBeInTheDocument();
  });

  it('stopPropagation prevents parent click', async () => {
    mockCopy.mockResolvedValue(true);
    const parentClick = vi.fn();
    render(
      <div onClick={parentClick}>
        <CopyButton text="x" />
      </div>,
    );
    await act(async () => {
      fireEvent.click(screen.getByRole('button'));
    });
    expect(parentClick).not.toHaveBeenCalled();
  });
});
