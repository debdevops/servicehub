import { render } from '@testing-library/react';
import { MessageListSkeleton } from '@/components/messages/MessageListSkeleton';

describe('MessageListSkeleton', () => {
  it('renders without crashing', () => {
    const { container } = render(<MessageListSkeleton />);
    expect(container.firstChild).toBeTruthy();
  });

  it('renders exactly 10 skeleton cards', () => {
    const { container } = render(<MessageListSkeleton />);
    const cards = container.querySelectorAll('.animate-pulse');
    expect(cards).toHaveLength(10);
  });

  it('skeleton cards have expected structure', () => {
    const { container } = render(<MessageListSkeleton />);
    const firstCard = container.querySelector('.animate-pulse');
    // Each card should have the bg-gray-200 shimmer bars
    expect(firstCard?.querySelectorAll('.bg-gray-200').length).toBeGreaterThan(0);
  });

  it('wraps content in a padded container', () => {
    const { container } = render(<MessageListSkeleton />);
    const wrapper = container.firstChild as HTMLElement;
    expect(wrapper.classList.contains('p-4')).toBe(true);
    expect(wrapper.classList.contains('space-y-4')).toBe(true);
  });
});
