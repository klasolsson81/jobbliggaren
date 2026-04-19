using JobbPilot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace JobbPilot.Application.UnitTests.Common;

internal static class TestAppDbContextFactory
{
    internal static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
