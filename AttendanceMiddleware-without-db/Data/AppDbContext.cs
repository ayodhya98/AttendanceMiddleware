using AttendanceMiddleware_without_db.Entities;
using Microsoft.EntityFrameworkCore;

namespace AttendanceMiddleware_without_db.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<EmployeeRegisteredMessage> RegisteredEmployees => Set<EmployeeRegisteredMessage>();
        public DbSet<AttendanceLog> AttendanceLogs => Set<AttendanceLog>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<EmployeeRegisteredMessage>()
                .HasIndex(e => new { e.EmpNo, e.CompanyCode })
                .IsUnique();
        }
    }
}