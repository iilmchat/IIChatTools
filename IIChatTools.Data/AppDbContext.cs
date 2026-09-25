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

        public DbSet<UserSetting> UserSettings { get; set; }

        /// <summary>
        /// Чанки документов для RAG (v1.5.0, KI-083, Шаг 2A).
        /// Embedding-векторы хранятся отдельно в <c>IVectorStore</c>.
        /// </summary>
        public DbSet<DocumentChunk> DocumentChunks { get; set; }

        /// <summary>
        /// Вложения к чатам (файлы, проиндексированные в my_rag_docs).
        /// v1.5.0, KI-083, Шаг 6A.
        /// </summary>
        public DbSet<ChatAttachment> ChatAttachments { get; set; }

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

            modelBuilder.Entity<UserSetting>(entity =>
            {
                entity.ToTable("UserSettings");
                entity.HasIndex(s => new { s.UserId, s.Key }).IsUnique();
                entity.Property(s => s.Key).IsRequired().HasMaxLength(200);
                entity.Property(s => s.Value).HasMaxLength(4000);
                entity.Property(s => s.Type).HasMaxLength(20);
                entity.HasOne(s => s.User)
                    .WithMany()
                    .HasForeignKey(s => s.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ============ DocumentChunks (v1.5.0, KI-083, Шаг 2A) ============
            modelBuilder.Entity<DocumentChunk>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.IndexName)
                    .HasMaxLength(50)
                    .IsRequired();

                entity.Property(e => e.DocumentPath)
                    .HasMaxLength(500)
                    .IsRequired();

                entity.Property(e => e.DocumentHash)
                    .HasMaxLength(64)   // SHA256 hex = 64 символа
                    .IsRequired();

                entity.Property(e => e.Text);            // nvarchar(max)
                entity.Property(e => e.MetadataJson);    // nvarchar(max)

                // FK на Chat с каскадным удалением:
                // удаление чата → удаление чанков my_rag_docs этого чата.
                // ChatId nullable — для project_docs / chat_history / workspace.
                entity.HasOne(e => e.Chat)
                    .WithMany()
                    .HasForeignKey(e => e.ChatId)
                    .OnDelete(DeleteBehavior.Cascade)
                    .IsRequired(false);

                // Индекс 1: проверка «уже индексировался» (skip re-index по hash).
                entity.HasIndex(e => new { e.IndexName, e.DocumentHash })
                    .HasDatabaseName("IX_DocumentChunks_Index_Hash");

                // Индекс 2: фильтрация чанков при поиске (по индексу + чату + юзеру).
                entity.HasIndex(e => new { e.IndexName, e.ChatId, e.UserId })
                    .HasDatabaseName("IX_DocumentChunks_Index_Chat_User");

                // Индекс 3: удаление чанков документа при переиндексации.
                entity.HasIndex(e => e.DocumentPath)
                    .HasDatabaseName("IX_DocumentChunks_DocumentPath");

                // Примечание: FK на ApplicationUser НЕТ — UserId = 0
                // используется как маркер «глобальный чанк» (project_docs).
            });

            // ============ ChatAttachments (v1.5.0, KI-083, Шаг 6A) ============
            modelBuilder.Entity<ChatAttachment>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.FileName)
                    .HasMaxLength(255)
                    .IsRequired();

                entity.Property(e => e.ContentType)
                    .HasMaxLength(100);

                entity.Property(e => e.StoragePath)
                    .HasMaxLength(500)
                    .IsRequired();

                entity.Property(e => e.ContentHash)
                    .HasMaxLength(64)   // SHA256 hex = 64 символа
                    .IsRequired();

                // FK на Chat с каскадным удалением:
                // удаление чата → удаление записи вложения.
                // Физические файлы + чанки — очищаются отдельно
                // (ChatAttachmentService.ClearForChatAsync / DeleteAsync).
                entity.HasOne(e => e.Chat)
                    .WithMany()
                    .HasForeignKey(e => e.ChatId)
                    .OnDelete(DeleteBehavior.Cascade);

                // Индекс 1: список вложений чата (для UI-чипов).
                entity.HasIndex(e => e.ChatId)
                    .HasDatabaseName("IX_ChatAttachments_ChatId");

                // Индекс 2: дедупликация по (UserId, ContentHash).
                entity.HasIndex(e => new { e.UserId, e.ContentHash })
                    .HasDatabaseName("IX_ChatAttachments_User_Hash");
            });
        }
    }
}