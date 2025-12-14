using PaymentModule.Domain.Entities;

namespace PaymentModule.Infrastructure.Persistence.DbContext;

public class SecureDbContext : Microsoft.EntityFrameworkCore.DbContext
{
    public SecureDbContext(Microsoft.EntityFrameworkCore.DbContextOptions<SecureDbContext> options) : base(options) { }

    public Microsoft.EntityFrameworkCore.DbSet<Wallet> Wallets => Set<Wallet>();
    public Microsoft.EntityFrameworkCore.DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SecureDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
