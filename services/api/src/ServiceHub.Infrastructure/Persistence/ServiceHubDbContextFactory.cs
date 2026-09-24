using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Design-time only: lets <c>dotnet ef migrations add</c> build the context without starting the
/// host (which would take the instance lock). Never used at runtime.
/// </summary>
internal sealed class ServiceHubDbContextFactory : IDesignTimeDbContextFactory<ServiceHubDbContext>
{
    public ServiceHubDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ServiceHubDbContext>()
            .UseSqlite("Data Source=design-time-only.db")
            .Options);
}
