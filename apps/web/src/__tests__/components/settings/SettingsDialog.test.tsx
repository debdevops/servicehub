import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SettingsDialog } from '@/components/settings/SettingsDialog';
import { saveApiKey, loadApiKey, clearApiKey, hasStoredKey } from '@/lib/apiKeyStore';

// Mock the entire apiKeyStore so tests never touch real crypto or sessionStorage.
// This also breaks the CodeQL taint flow in the source under test.
vi.mock('@/lib/apiKeyStore', () => ({
  saveApiKey: vi.fn().mockResolvedValue(undefined),
  loadApiKey: vi.fn().mockResolvedValue(null),
  clearApiKey: vi.fn(),
  hasStoredKey: vi.fn().mockReturnValue(false),
  getCachedApiKey: vi.fn().mockReturnValue(null),
}));

describe('SettingsDialog', () => {
  const onClose = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    // Restore default mock return values after clearAllMocks resets them
    vi.mocked(loadApiKey).mockResolvedValue(null);
    vi.mocked(hasStoredKey).mockReturnValue(false);
    vi.mocked(saveApiKey).mockResolvedValue(undefined);
  });

  afterEach(() => {
    // nothing — no real sessionStorage used in these tests
  });

  it('renders nothing when isOpen is false', () => {
    const { container } = render(<SettingsDialog isOpen={false} onClose={onClose} />);
    expect(container.firstChild).toBeNull();
  });

  it('renders dialog when isOpen is true', () => {
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText('Security Settings')).toBeInTheDocument();
  });

  it('renders API Key input with label', () => {
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    expect(screen.getByText('API Key')).toBeInTheDocument();
    expect(screen.getByPlaceholderText(/Paste your API key/)).toBeInTheDocument();
  });

  it('loads existing key from sessionStorage on open', async () => {
    vi.mocked(loadApiKey).mockResolvedValue('test-key-abc');
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    // loadApiKey() is async; waitFor flushes the Promise and React state update
    await waitFor(() => {
      const input = screen.getByPlaceholderText(/Paste your API key/) as HTMLInputElement;
      expect(input.value).toBe('test-key-abc');
    });
  });

  it('shows status indicator when key is active', () => {
    vi.mocked(hasStoredKey).mockReturnValue(true);
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    expect(screen.getByText('API key is active for this session')).toBeInTheDocument();
  });

  it('saves key to sessionStorage on Save click', async () => {
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    const input = screen.getByPlaceholderText(/Paste your API key/);
    await userEvent.type(input, 'my-new-key-xyz');
    fireEvent.click(screen.getByText('Save'));
    expect(vi.mocked(saveApiKey)).toHaveBeenCalledWith('my-new-key-xyz');
    expect(screen.getByText('Saved!')).toBeInTheDocument();
  });

  it('saves key on Enter key press', async () => {
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    const input = screen.getByPlaceholderText(/Paste your API key/);
    await userEvent.type(input, 'enter-key-test');
    fireEvent.keyDown(input, { key: 'Enter' });
    expect(vi.mocked(saveApiKey)).toHaveBeenCalledWith('enter-key-test');
  });

  it('clears key on Clear button click', async () => {
    vi.mocked(loadApiKey).mockResolvedValue('old-key');
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    // Wait for the async load to populate the input (enables the Clear button)
    await waitFor(() => {
      const input = screen.getByPlaceholderText(/Paste your API key/) as HTMLInputElement;
      expect(input.value).toBe('old-key');
    });
    fireEvent.click(screen.getByText('Clear key'));
    expect(vi.mocked(clearApiKey)).toHaveBeenCalled();
  });

  it('closes on Escape key', () => {
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalled();
  });

  it('closes when clicking backdrop', () => {
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    // Backdrop has aria-hidden="true"
    const backdrop = screen.getByRole('dialog').querySelector('[aria-hidden="true"]');
    expect(backdrop).not.toBeNull();
    fireEvent.click(backdrop!);
    expect(onClose).toHaveBeenCalled();
  });

  it('closes when clicking Cancel', () => {
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    fireEvent.click(screen.getByText('Cancel'));
    expect(onClose).toHaveBeenCalled();
  });

  it('closes when clicking X button', () => {
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    fireEvent.click(screen.getByLabelText('Close settings'));
    expect(onClose).toHaveBeenCalled();
  });

  it('toggles password visibility', async () => {
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    const input = screen.getByPlaceholderText(/Paste your API key/) as HTMLInputElement;
    expect(input.type).toBe('password');

    fireEvent.click(screen.getByLabelText('Show API key'));
    expect(input.type).toBe('text');

    fireEvent.click(screen.getByLabelText('Hide API key'));
    expect(input.type).toBe('password');
  });

  it('removes key from sessionStorage when saving empty value', async () => {
    vi.mocked(loadApiKey).mockResolvedValue('will-be-removed');
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    await waitFor(() => {
      const input = screen.getByPlaceholderText(/Paste your API key/) as HTMLInputElement;
      expect(input.value).toBe('will-be-removed');
    });
    const input = screen.getByPlaceholderText(/Paste your API key/) as HTMLInputElement;
    await userEvent.clear(input);
    fireEvent.click(screen.getByText('Save'));
    // saveApiKey('') internally delegates to clearApiKey — verify it was called with empty
    expect(vi.mocked(saveApiKey)).toHaveBeenCalledWith('');
  });

  it('disables Clear button when input is empty', () => {
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    const clearBtn = screen.getByText('Clear key').closest('button');
    expect(clearBtn).toBeDisabled();
  });

  it('shows info box with provisioning instructions', () => {
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    expect(screen.getByText('Where do I get an API key?')).toBeInTheDocument();
    expect(screen.getAllByText(/ScopedApiKey/).length).toBeGreaterThan(0);
  });

  it('explains sessionStorage usage', () => {
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    expect(screen.getByText(/encrypted with AES-GCM and stored in this browser tab/)).toBeInTheDocument();
    expect(screen.getByText(/X-API-Key/)).toBeInTheDocument();
  });

  it('shows Saved state after save', () => {
    render(<SettingsDialog isOpen={true} onClose={onClose} />);
    const input = screen.getByPlaceholderText(/Paste your API key/);
    fireEvent.change(input, { target: { value: 'test-key' } });
    fireEvent.click(screen.getByText('Save'));
    expect(screen.getByText('Saved!')).toBeInTheDocument();
    expect(vi.mocked(saveApiKey)).toHaveBeenCalledWith('test-key');
  });
});
