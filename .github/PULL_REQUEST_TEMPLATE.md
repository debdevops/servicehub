
## What does this PR do?

<!-- One or two sentences: what changed and why. Link an issue if one exists. -->

## Type of change

- [ ] Bug fix
- [ ] New feature
- [ ] Documentation
- [ ] Refactor / cleanup
- [ ] Other (describe above)

## Checklist

Run these from `archive/servicehub-4.0.0/` (the archived 4.0.0 codebase — ADR-0012/0013).

- [ ] `dotnet build services/api/ServiceHub.sln --configuration Release` passes with zero warnings
- [ ] `npm run -w apps/web build` passes with zero warnings
- [ ] Backend unit/integration tests pass (`dotnet test`)
- [ ] Frontend tests pass (`npm run -w apps/web test`)
- [ ] New/changed behavior has test coverage
- [ ] No secrets, connection strings, or credentials added to source or logs
- [ ] Docs updated if user-facing behavior changed (README, CHANGELOG, or `docs/`)

## Screenshots (UI changes only)

<!-- Before/after screenshots or a short clip, if applicable. -->
