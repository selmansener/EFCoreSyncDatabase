using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class TargetDbContext : BaseAppDbContext
{
    public TargetDbContext(DbContextOptions<TargetDbContext> options) : base(options)
    {
    }
}

