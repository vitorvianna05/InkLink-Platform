using InkLink.Messaging.Domain;
using Microsoft.EntityFrameworkCore;

namespace InkLink.Messaging.Infrastructure.Persistence;

public sealed class MessagingDbContext(DbContextOptions<MessagingDbContext> options) : DbContext(options)
{
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<InboxRecord> Inbox => Set<InboxRecord>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.HasDefaultSchema("messaging");
        model.Entity<Conversation>(entity =>
        {
            entity.ToTable("conversations", table => table.HasCheckConstraint("ck_distinct_participants", "\"ClientId\" <> \"ArtistAccountId\""));
            entity.HasKey(c => c.Id); entity.Property(c => c.Id).ValueGeneratedNever();
            entity.HasIndex(c => c.EngagementId).IsUnique();
            entity.HasIndex(c => new { c.ClientId, c.Id }); entity.HasIndex(c => new { c.ArtistAccountId, c.Id });
        });
        model.Entity<Message>(entity =>
        {
            entity.ToTable("messages"); entity.HasKey(m => m.Id); entity.Property(m => m.Id).ValueGeneratedNever();
            entity.HasOne<Conversation>().WithMany().HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(m => new { m.ConversationId, m.Sequence }).IsUnique();
            entity.HasIndex(m => new { m.ConversationId, m.AuthorId, m.RequestKey }).IsUnique();
            entity.Property(m => m.Body).HasMaxLength(4000).IsRequired();
        });
        model.Entity<InboxRecord>(entity =>
        {
            entity.ToTable("inbox"); entity.HasKey(i => i.Id); entity.Property(i => i.Id).ValueGeneratedNever();
            entity.Property(i => i.Fingerprint).HasMaxLength(64).IsRequired();
        });
    }
}

public sealed class InboxRecord
{
    public Guid Id { get; set; }
    public string Fingerprint { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; }
}
