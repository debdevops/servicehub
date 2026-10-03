// Lints tests/web and tests/e2e with the same rules as the app (apps/servicehub/eslint.config.js).
// ESLint refuses files outside the config's base path, so the tests folder carries its own entry point.
import base from '../apps/servicehub/eslint.config.js'

export default [...base, { ignores: ['**/coverage/**', '**/playwright-report/**', '**/test-results/**', 'backend/**'] }]
