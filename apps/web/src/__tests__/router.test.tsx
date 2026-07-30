import { describe, it, expect } from 'vitest';
import type { ReactElement } from 'react';
import { Navigate } from 'react-router-dom';
import { router } from '@/router';
import { RouteErrorPage } from '@/pages/RouteErrorPage';
import { NotFoundPage } from '@/pages/NotFoundPage';

// Router-config regression guard: demo trees previously redirected to their own index on
// error (an infinite-loop hazard if the index itself errors), and the 404 route silently
// teleported to /welcome. These tests check the wiring, not rendering — rendering behavior
// is covered by RouteErrorPage.test.tsx and NotFoundPage.test.tsx.

const DEMO_PATHS = ['/demo/azure', '/demo/aws', '/demo/gcp'];

describe('router error wiring', () => {
  it('every demo tree uses RouteErrorPage as its errorElement, not a self-redirect', () => {
    const demoRoutes = router.routes.filter((r) => DEMO_PATHS.includes(r.path ?? ''));
    expect(demoRoutes).toHaveLength(DEMO_PATHS.length);

    for (const route of demoRoutes) {
      const errorElement = route.errorElement as ReactElement | undefined;
      expect(errorElement?.type).toBe(RouteErrorPage);
      expect(errorElement?.type).not.toBe(Navigate);
    }
  });

  it('the main app tree uses RouteErrorPage as its errorElement', () => {
    const mainRoute = router.routes.find((r) => r.path === '/' && Array.isArray(r.children));
    expect(mainRoute).toBeDefined();
    const errorElement = mainRoute?.errorElement as ReactElement | undefined;
    expect(errorElement?.type).toBe(RouteErrorPage);
  });

  it('the 404 fallback renders NotFoundPage rather than redirecting', () => {
    // Pathless layout route (no `path` key) wrapping a `path: '*'` child — see the
    // comment in router.tsx for why the wildcard can't live on the layout route itself.
    const fallbackLayoutRoute = router.routes.find((r) => r.path === undefined && Array.isArray(r.children));
    expect(fallbackLayoutRoute).toBeDefined();

    const wildcardChild = fallbackLayoutRoute?.children?.find((c) => c.path === '*');
    expect(wildcardChild).toBeDefined();

    const childElement = wildcardChild?.element as ReactElement | undefined;
    expect(childElement?.type).toBe(NotFoundPage);
    expect(childElement?.type).not.toBe(Navigate);
  });
});
