import { describe, it, expect, vi } from 'vitest';
import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Pagination } from '@/components/grid/Pagination';

const defaultProps = {
  currentPage: 1,
  totalPages: 5,
  pageSize: 25,
  totalItems: 120,
  onPageChange: vi.fn(),
  onPageSizeChange: vi.fn(),
};

function setup(props = {}) {
  const onPageChange = vi.fn();
  const onPageSizeChange = vi.fn();
  const mergedProps = { ...defaultProps, onPageChange, onPageSizeChange, ...props };
  const utils = render(<Pagination {...mergedProps} />);
  return { ...utils, onPageChange, onPageSizeChange };
}

describe('Pagination', () => {
  // ── Item range text ───────────────────────────────────────────────────────

  it('shows the correct item range for the first page', () => {
    setup({ currentPage: 1, pageSize: 25, totalItems: 120 });
    expect(screen.getByText(/1–25 of 120/)).toBeInTheDocument();
  });

  it('shows the correct item range for the second page', () => {
    setup({ currentPage: 2, pageSize: 25, totalItems: 120 });
    expect(screen.getByText(/26–50 of 120/)).toBeInTheDocument();
  });

  it('shows the capped end-item on the last (partial) page', () => {
    setup({ currentPage: 5, pageSize: 25, totalItems: 120 });
    // Page 5: items 101–120 (not 101–125)
    expect(screen.getByText(/101–120 of 120/)).toBeInTheDocument();
  });

  // ── Page numbers ≤ 7 ─────────────────────────────────────────────────────

  it('shows all page numbers when totalPages ≤ 7', () => {
    setup({ currentPage: 1, totalPages: 5, totalItems: 125 });
    [1, 2, 3, 4, 5].forEach(n => {
      expect(screen.getByRole('button', { name: String(n) })).toBeInTheDocument();
    });
  });

  it('shows all 7 page numbers when totalPages = 7', () => {
    setup({ currentPage: 4, totalPages: 7, totalItems: 175 });
    [1, 2, 3, 4, 5, 6, 7].forEach(n => {
      expect(screen.getByRole('button', { name: String(n) })).toBeInTheDocument();
    });
  });

  // ── Ellipsis — near start ─────────────────────────────────────────────────

  it('shows [1 2 3 4 5 … 20] when on page 1 of 20', () => {
    setup({ currentPage: 1, totalPages: 20, totalItems: 500 });
    [1, 2, 3, 4, 5, 20].forEach(n => {
      expect(screen.getByRole('button', { name: String(n) })).toBeInTheDocument();
    });
    expect(screen.getByText('...')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '6' })).not.toBeInTheDocument();
  });

  it('shows [1 2 3 4 5 … 20] when on page 4 of 20', () => {
    setup({ currentPage: 4, totalPages: 20, totalItems: 500 });
    [1, 2, 3, 4, 5, 20].forEach(n => {
      expect(screen.getByRole('button', { name: String(n) })).toBeInTheDocument();
    });
    expect(screen.getByText('...')).toBeInTheDocument();
  });

  // ── Ellipsis — near end ───────────────────────────────────────────────────

  it('shows [1 … 16 17 18 19 20] when on page 17 of 20', () => {
    setup({ currentPage: 17, totalPages: 20, totalItems: 500 });
    [1, 16, 17, 18, 19, 20].forEach(n => {
      expect(screen.getByRole('button', { name: String(n) })).toBeInTheDocument();
    });
    expect(screen.getByText('...')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '2' })).not.toBeInTheDocument();
  });

  it('shows [1 … 16 17 18 19 20] when on the last page of 20', () => {
    setup({ currentPage: 20, totalPages: 20, totalItems: 500 });
    [1, 16, 17, 18, 19, 20].forEach(n => {
      expect(screen.getByRole('button', { name: String(n) })).toBeInTheDocument();
    });
  });

  // ── Ellipsis — middle ─────────────────────────────────────────────────────

  it('shows [1 … 9 10 11 … 20] when on page 10 of 20', () => {
    setup({ currentPage: 10, totalPages: 20, totalItems: 500 });
    [1, 9, 10, 11, 20].forEach(n => {
      expect(screen.getByRole('button', { name: String(n) })).toBeInTheDocument();
    });
    // Should have two ellipsis markers
    expect(screen.getAllByText('...')).toHaveLength(2);
  });

  // ── Prev / Next button states ─────────────────────────────────────────────

  it('disables the Previous button on the first page', () => {
    setup({ currentPage: 1 });
    expect(screen.getByTitle('Previous page')).toBeDisabled();
  });

  it('enables the Previous button when not on the first page', () => {
    setup({ currentPage: 3 });
    expect(screen.getByTitle('Previous page')).not.toBeDisabled();
  });

  it('disables the Next button on the last page', () => {
    setup({ currentPage: 5, totalPages: 5 });
    expect(screen.getByTitle('Next page')).toBeDisabled();
  });

  it('enables the Next button when not on the last page', () => {
    setup({ currentPage: 2, totalPages: 5 });
    expect(screen.getByTitle('Next page')).not.toBeDisabled();
  });

  // ── onPageChange callbacks ────────────────────────────────────────────────

  it('calls onPageChange with page - 1 when Previous is clicked', async () => {
    const { onPageChange } = setup({ currentPage: 3, totalPages: 5 });
    await userEvent.click(screen.getByTitle('Previous page'));
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  it('calls onPageChange with page + 1 when Next is clicked', async () => {
    const { onPageChange } = setup({ currentPage: 3, totalPages: 5 });
    await userEvent.click(screen.getByTitle('Next page'));
    expect(onPageChange).toHaveBeenCalledWith(4);
  });

  it('calls onPageChange with the correct page number when a page button is clicked', async () => {
    const { onPageChange } = setup({ currentPage: 1, totalPages: 5 });
    await userEvent.click(screen.getByRole('button', { name: '3' }));
    expect(onPageChange).toHaveBeenCalledWith(3);
  });

  // ── onPageSizeChange callback ─────────────────────────────────────────────

  it('calls onPageSizeChange when a different page size is selected', async () => {
    const { onPageSizeChange } = setup({ pageSize: 25 });
    await userEvent.selectOptions(screen.getByRole('combobox'), '50');
    expect(onPageSizeChange).toHaveBeenCalledWith(50);
  });

  it('renders page size options 25, 50, and 100', () => {
    setup();
    const select = screen.getByRole('combobox');
    [25, 50, 100].forEach(size => {
      expect(within(select).getByRole('option', { name: String(size) })).toBeInTheDocument();
    });
  });

  // ── Active page highlight ─────────────────────────────────────────────────

  it('highlights the current page button with a distinct class', () => {
    setup({ currentPage: 3, totalPages: 5 });
    const activeBtn = screen.getByRole('button', { name: '3' });
    expect(activeBtn.className).toContain('bg-primary-500');
  });

  it('does not highlight non-current page buttons', () => {
    setup({ currentPage: 3, totalPages: 5 });
    const inactiveBtn = screen.getByRole('button', { name: '2' });
    expect(inactiveBtn.className).not.toContain('bg-primary-500');
  });
});
