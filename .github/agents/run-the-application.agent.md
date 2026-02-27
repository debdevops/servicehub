---
name: run-the-application
description: Comprehensive application startup and validation agent. Checks all prerequisites, validates builds, fixes issues, and launches both backend API and frontend.
argument-hint: No arguments required. Runs full startup sequence.
tools: ['vscode', 'execute', 'read', 'agent', 'edit', 'search', 'todo']
---

# ServiceHub Application Startup Agent

Fully automated application startup with comprehensive validation, issue detection, and fixing.

## Capabilities

### 1. **Prerequisite Validation** ✅
- Check .NET 8 SDK installed
- Check Node.js 18+ installed
- Check git available
- Verify workspace structure
- Check required files exist (package.json, .csproj files)

### 2. **Dependency Installation** 📦
- Restore NuGet packages (dotnet restore)
- Install npm dependencies (npm install)
- Ensure database migrations ready

### 3. **Build Validation** 🏗️
- Backend: `dotnet build` in services/api with 0 errors
- Frontend: `npm run build` in apps/web with 0 errors
- Detect and report build failures
- Automatic issue fixing when possible

### 4. **Issue Detection & Fixing** 🔧
- Check for unused imports (Pylance unused-imports refactoring)
- Validate TypeScript compilation
- Check for dotnet warnings
- Fix import formatting issues
- Validate all config files

### 5. **Application Startup** 🚀
- Start backend API on port 5153 (production mode)
- Start frontend on port 3000 (dev mode with hmr)
- Verify both services are running
- Monitor for startup errors
- Provide connection URLs

### 6. **Health Checks** 💚
- Backend: GET /api/v1/health/ready
- Frontend: Check port 3000 responding
- Database: Verify SQLite DLQ database
- Report overall health status

### 7. **Status Summary** 📊
Provides detailed report:
- ✅ Passed prerequisites
- ✅ Build status (backend/frontend)
- ✅ Launch status
- ✅ Running URLs
- ❌ Any unresolved issues

## Execution Steps

1. **Check prerequisites** → Error if missing
2. **Validate workspace** → Check structure
3. **Install dependencies** → NuGet + npm
4. **Validate builds** → Both must pass
5. **Fix any issues** → Auto-remediate if possible
6. **Start backend** → API server
7. **Start frontend** → React dev server
8. **Health checks** → Verify running
9. **Report summary** → Full status

## What It Does

- Detects and fixes broken changes from recent code updates
- Validates all types (TypeScript, C#)
- Runs in non-blocking mode (background processes)
- Provides real-time feedback on each step
- Auto-recovers from common issues
- Maintains persistent state between checks

## Usage

Simply invoke this agent with no arguments. It will:
1. Run through all validation steps
2. Report any issues found
3. Attempt automatic fixes
4. Launch both services
5. Display running status

Best used after major code changes or fresh clones.