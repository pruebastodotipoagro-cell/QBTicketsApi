using Microsoft.EntityFrameworkCore;
using QBTicketsApi.Models;

namespace QBTicketsApi.Database
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(
            DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<QuickBooksConnection>
            QuickBooksConnections
        { get; set; }

        public DbSet<User>
            Users
        { get; set; }

        public DbSet<Invoice>
            Invoices
        { get; set; }

        public DbSet<InvoiceLine>
            InvoiceLines
        { get; set; }

        public DbSet<Customer>
            Customers
        { get; set; }

        public DbSet<CashMovement>
            CashMovements
        { get; set; }

        protected override void OnModelCreating(
            ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Invoice>()
                .HasIndex(x => x.QuickBooksId);

            modelBuilder.Entity<Invoice>()
                .HasIndex(x => new
                {
                    x.QuickBooksId,
                    x.CreatedAt
                });

            modelBuilder.Entity<Invoice>()
                .HasMany(x => x.Lines)
                .WithOne(x => x.Invoice)
                .HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<InvoiceLine>()
                .HasIndex(x => new
                {
                    x.InvoiceId,
                    x.QuickBooksLineId
                });

            modelBuilder.Entity<CashMovement>()
                .HasIndex(x => new
                {
                    x.CashierName,
                    x.MovementDate,
                    x.MovementType
                });
        }
    }
}