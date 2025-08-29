using Microsoft.EntityFrameworkCore;

namespace Api.Data;

public class SourceDbContext : BaseAppDbContext
{
    public SourceDbContext(DbContextOptions<SourceDbContext> options) : base(options)
    {
    }
}

