import { vi, describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { HelpPage } from '@/pages/HelpPage';
import { helpSections } from '@/lib/helpContent';
import userEvent from '@testing-library/user-event';

// Mock GuidedTour exports
vi.mock('@/components/help/GuidedTour', () => ({
  resetTour: vi.fn(),
  isTourCompleted: vi.fn(() => true),
  GuidedTour: () => null,
}));

function renderHelpPage() {
  return render(
    <MemoryRouter>
      <HelpPage />
    </MemoryRouter>,
  );
}

describe('HelpPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders the help page header', () => {
    renderHelpPage();
    expect(screen.getByText('Help & Support')).toBeInTheDocument();
  });

  it('renders the search input', () => {
    renderHelpPage();
    expect(screen.getByPlaceholderText(/Search help topics/)).toBeInTheDocument();
  });

  it('renders all help sections', () => {
    renderHelpPage();
    helpSections.forEach((section) => {
      // Some titles may appear multiple times (e.g., in section header + items)
      expect(screen.getAllByText(section.title).length).toBeGreaterThan(0);
    });
  });

  it('renders the description text', () => {
    renderHelpPage();
    expect(
      screen.getByText(/Everything you need to debug Azure Service Bus/),
    ).toBeInTheDocument();
  });

  it('renders the "Take a Tour" button', () => {
    renderHelpPage();
    expect(screen.getByText('Take a Tour')).toBeInTheDocument();
  });

  it('dispatches custom event on "Take a Tour" click', () => {
    const dispatchSpy = vi.spyOn(window, 'dispatchEvent');
    renderHelpPage();
    fireEvent.click(screen.getByText('Take a Tour'));
    expect(dispatchSpy).toHaveBeenCalledWith(
      expect.objectContaining({ type: 'servicehub:start-tour' }),
    );
  });

  it('filters sections by search query', async () => {
    renderHelpPage();
    const input = screen.getByPlaceholderText(/Search help topics/);
    // Pick a unique question from the first section
    const firstQuestion = helpSections[0].items[0].question;
    await userEvent.type(input, firstQuestion.slice(0, 10));
    // At minimum the first section's first item should still be visible
    expect(screen.getByText(firstQuestion)).toBeInTheDocument();
  });

  it('shows no-results message when search matches nothing', async () => {
    renderHelpPage();
    const input = screen.getByPlaceholderText(/Search help topics/);
    await userEvent.type(input, 'xyznosuchtopic123');
    expect(screen.getByText(/No results for/)).toBeInTheDocument();
  });

  it('clears search when X button is clicked', async () => {
    renderHelpPage();
    const input = screen.getByPlaceholderText(/Search help topics/);
    await userEvent.type(input, 'something');
    // Should see clear button
    const clearButton = screen.getByLabelText('Clear search');
    fireEvent.click(clearButton);
    expect((input as HTMLInputElement).value).toBe('');
  });

  it('toggles section expand/collapse', () => {
    renderHelpPage();
    const sectionTitle = helpSections[0].title;
    const firstQuestion = helpSections[0].items[0].question;

    // Sections start expanded
    expect(screen.getByText(firstQuestion)).toBeInTheDocument();

    // Click to collapse
    fireEvent.click(screen.getByText(sectionTitle));

    // The question should be hidden
    expect(screen.queryByText(firstQuestion)).not.toBeInTheDocument();

    // Click to expand again
    fireEvent.click(screen.getByText(sectionTitle));
    expect(screen.getByText(firstQuestion)).toBeInTheDocument();
  });

  it('displays item counts in sections', () => {
    renderHelpPage();
    // Item counts are displayed in the section header - verify at least one is present
    const section = helpSections[0];
    const countText = `${section.items.length} ${section.items.length === 1 ? 'topic' : 'topics'}`;
    // The count appears in the subtitle of the section header
    expect(screen.getAllByText(new RegExp(countText)).length).toBeGreaterThan(0);
  });
});
