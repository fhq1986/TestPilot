using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace AI.TestPlatform.Infrastructure.Data;

public class TestDbContextFactory : IDesignTimeDbContextFactory<TestDbContext>
{
    public TestDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TestDbContext>();
        // UseVector 必须与运行时一致：设计时模型少了向量映射，dotnet ef 会拒绝生成迁移
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=ai_test;Username=postgres;Password=postgres", npgsql => npgsql.UseVector());
        return new TestDbContext(optionsBuilder.Options);
    }
}
