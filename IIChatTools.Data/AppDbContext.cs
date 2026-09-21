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

        /// <summary>
        /// Чаты пользователей.
        /// </summary>
        public DbSet<Chat> Chats { get; set; }

        /// <summary>
        /// Сообщения чатов.
        /// </summary>
        public DbSet<ChatMessage> ChatMessages { get; set; }

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

            // ============ Chats ============
            modelBuilder.Entity<Chat>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Title).HasMaxLength(200).IsRequired();
                entity.Property(e => e.Model).HasMaxLength(100);
                entity.Property(e => e.SystemPrompt).HasMaxLength(4000);

                // FK на пользователя с каскадным удалением
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Индекс для sidebar (сортировка по UpdatedAt)
                entity.HasIndex(e => new { e.UserId, e.UpdatedAt })
                    .HasDatabaseName("IX_Chats_UserId_UpdatedAt");
            });

            // ============ ChatMessages ============
            modelBuilder.Entity<ChatMessage>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Role).HasMaxLength(20).IsRequired();
                entity.Property(e => e.Content); // nvarchar(max) для длинных ответов
                entity.Property(e => e.ToolCallId).HasMaxLength(100);
                entity.Property(e => e.ToolName).HasMaxLength(100);
                // ToolCallsJson — без MaxLength (может быть большой)

                entity.HasOne(e => e.Chat)
                    .WithMany(c => c.Messages)
                    .HasForeignKey(e => e.ChatId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Индекс для загрузки истории чата
                entity.HasIndex(e => new { e.ChatId, e.CreatedAt })
                    .HasDatabaseName("IX_ChatMessages_ChatId_CreatedAt");
            });                
        }
    }
}