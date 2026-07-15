using Microsoft.EntityFrameworkCore;

namespace ByHofman.EfCore.Provisioning.Tests.Infrastructure;

internal class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }
}

internal class SecondTestDbContext : DbContext
{
    public SecondTestDbContext(DbContextOptions<SecondTestDbContext> options) : base(options) { }
}
