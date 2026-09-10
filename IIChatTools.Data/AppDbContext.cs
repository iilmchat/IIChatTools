using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using IIChatTools.Data.Entities;

namespace IIChatTools.Data
{
    /// <summary>
    /// Контекст базы данных приложения
    /// </summary>
    public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<AppSetting> AppSettings { get; set; }
        public DbSet<PendingAction> PendingActions { get; set; }
        public DbSet<AgentState> AgentStates { get; set; }
        public DbSet<MemoryEntry> MemoryEntries { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Настройка связей и ограничений
            modelBuilder.Entity<AuditLog>()
                .HasOne(a => a.User)
                .WithMany(u => u.AuditLogs)
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PendingAction>()
                .HasOne(p => p.User)
                .WithMany(u => u.PendingActions)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // Уникальность ключей настроек
            modelBuilder.Entity<AppSetting>()
                .HasIndex(a => a.Key)
                .IsUnique();

            // Для AgentState и MemoryEntry связи с пользователем
            modelBuilder.Entity<AgentState>()
                .HasOne(a => a.User)
                .WithMany()
                .HasForeignKey(a => a.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MemoryEntry>()
                .HasOne(m => m.User)
                .WithMany()
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}