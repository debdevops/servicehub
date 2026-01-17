#!/bin/bash
set -e

echo "🚀 Setting up ServiceHub API Architecture..."

# Navigate to api folder
cd services/api

# Create solution
echo "📦 Creating solution..."
dotnet new sln -n ServiceHub

# Create projects
echo "📦 Creating projects..."
dotnet new webapi -n ServiceHub.Api -o src/ServiceHub.Api -f net8.0
dotnet new classlib -n ServiceHub.Core -o src/ServiceHub.Core -f net8.0
dotnet new classlib -n ServiceHub.Infrastructure -o src/ServiceHub.Infrastructure -f net8.0
dotnet new classlib -n ServiceHub.Shared -o src/ServiceHub.Shared -f net8.0
dotnet new xunit -n ServiceHub.UnitTests -o tests/ServiceHub.UnitTests -f net8.0
dotnet new xunit -n ServiceHub.IntegrationTests -o tests/ServiceHub.IntegrationTests -f net8.0

# Add to solution
echo "📦 Adding projects to solution..."
dotnet sln ServiceHub.sln add \
  src/ServiceHub.Api/ServiceHub.Api.csproj \
  src/ServiceHub.Core/ServiceHub.Core.csproj \
  src/ServiceHub.Infrastructure/ServiceHub.Infrastructure.csproj \
  src/ServiceHub.Shared/ServiceHub.Shared.csproj \
  tests/ServiceHub.UnitTests/ServiceHub.UnitTests.csproj \
  tests/ServiceHub.IntegrationTests/ServiceHub.IntegrationTests.csproj

# Add references
echo "🔗 Adding project references..."
dotnet add src/ServiceHub.Api/ServiceHub.Api.csproj reference \
  src/ServiceHub.Core/ServiceHub.Core.csproj \
  src/ServiceHub.Infrastructure/ServiceHub.Infrastructure.csproj \
  src/ServiceHub.Shared/ServiceHub.Shared.csproj

dotnet add src/ServiceHub.Core/ServiceHub.Core.csproj reference \
  src/ServiceHub.Shared/ServiceHub.Shared.csproj

dotnet add src/ServiceHub.Infrastructure/ServiceHub.Infrastructure.csproj reference \
  src/ServiceHub.Core/ServiceHub.Core.csproj \
  src/ServiceHub.Shared/ServiceHub.Shared.csproj

dotnet add tests/ServiceHub.UnitTests/ServiceHub.UnitTests.csproj reference \
  src/ServiceHub.Core/ServiceHub.Core.csproj \
  src/ServiceHub.Infrastructure/ServiceHub.Infrastructure.csproj \
  src/ServiceHub.Shared/ServiceHub.Shared.csproj

dotnet add tests/ServiceHub.IntegrationTests/ServiceHub.IntegrationTests.csproj reference \
  src/ServiceHub.Api/ServiceHub.Api.csproj

# Install packages
echo "📦 Installing NuGet packages..."
dotnet add src/ServiceHub.Api/ServiceHub.Api.csproj package Swashbuckle.AspNetCore
dotnet add src/ServiceHub.Api/ServiceHub.Api.csproj package FluentValidation.AspNetCore
dotnet add src/ServiceHub.Api/ServiceHub.Api.csproj package Serilog.AspNetCore
dotnet add src/ServiceHub.Api/ServiceHub.Api.csproj package AspNetCore.HealthChecks.UI.Client

dotnet add src/ServiceHub.Core/ServiceHub.Core.csproj package FluentValidation

dotnet add src/ServiceHub.Infrastructure/ServiceHub.Infrastructure.csproj package Azure.Messaging.ServiceBus
dotnet add src/ServiceHub.Infrastructure/ServiceHub.Infrastructure.csproj package Microsoft.Extensions.Caching.Memory
dotnet add src/ServiceHub.Infrastructure/ServiceHub.Infrastructure.csproj package Polly

dotnet add tests/ServiceHub.UnitTests/ServiceHub.UnitTests.csproj package FluentAssertions
dotnet add tests/ServiceHub.UnitTests/ServiceHub.UnitTests.csproj package Moq

# Create folders
echo "📁 Creating folder structure..."
mkdir -p src/ServiceHub.Api/Controllers/v1
mkdir -p src/ServiceHub.Api/{Middleware,Filters,Extensions,Configuration}
mkdir -p src/ServiceHub.Core/{Entities,Enums,Services,Validators}
mkdir -p src/ServiceHub.Core/DTOs/{Requests,Responses}
mkdir -p src/ServiceHub.Core/Interfaces/{Services,Repositories,External}
mkdir -p src/ServiceHub.Infrastructure/{ServiceBus,Security,AI,BackgroundServices}
mkdir -p src/ServiceHub.Infrastructure/Persistence/{InMemory,Database}
mkdir -p src/ServiceHub.Shared/{Constants,Helpers,Results}

# Remove default files
echo "🗑️  Removing default files..."
rm -f src/ServiceHub.Api/Controllers/WeatherForecastController.cs
rm -f src/ServiceHub.Api/WeatherForecast.cs
rm -f src/ServiceHub.Core/Class1.cs
rm -f src/ServiceHub.Infrastructure/Class1.cs
rm -f src/ServiceHub.Shared/Class1.cs

# Build to verify
echo "🏗️  Building solution..."
dotnet build ServiceHub.sln

echo "✅ Setup complete!"
echo ""
echo "📊 Solution structure:"
dotnet sln ServiceHub.sln list