using Microsoft.EntityFrameworkCore;
using Talkie.Shared;

namespace Talkie.Data;

public sealed class TalkieDbContext(DbContextOptions<TalkieDbContext> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserAvatar> UserAvatars => Set<UserAvatar>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<OwnedItem> OwnedItems => Set<OwnedItem>();
    public DbSet<UnopenedBox> UnopenedBoxes => Set<UnopenedBox>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationMember> ConversationMembers => Set<ConversationMember>();
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<ChannelVisibleRole> ChannelVisibleRoles => Set<ChannelVisibleRole>();
    public DbSet<ChannelPermission> ChannelPermissions => Set<ChannelPermission>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Reaction> Reactions => Set<Reaction>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<AiMessage> AiMessages => Set<AiMessage>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Company>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
            e.HasMany(x => x.Members).WithOne(x => x.Company).HasForeignKey(x => x.CompanyId);
            e.HasMany(x => x.Roles).WithOne(x => x.Company).HasForeignKey(x => x.CompanyId);
            e.HasOne(x => x.Server).WithOne(x => x.Company).HasForeignKey<Server>(x => x.CompanyId);
        });

        b.Entity<Role>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(50);
            e.Property(x => x.Color).HasMaxLength(20);
            e.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique();
        });

        b.Entity<User>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(256);
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Username).HasMaxLength(50);
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.AvatarContentType).HasMaxLength(100);
            e.Property(x => x.ActiveEffect).HasMaxLength(50);
            e.HasMany(x => x.OwnedItems).WithOne().HasForeignKey(x => x.UserId);
        });

        b.Entity<UserAvatar>(e =>
        {
            e.HasKey(x => x.UserId);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.HasOne<User>().WithOne().HasForeignKey<UserAvatar>(x => x.UserId);
        });

        b.Entity<UserRole>(e =>
        {
            e.HasKey(x => new { x.UserId, x.RoleId });
            e.HasOne(x => x.User).WithMany(x => x.Roles).HasForeignKey(x => x.UserId);
            e.HasOne(x => x.Role).WithMany(x => x.Members).HasForeignKey(x => x.RoleId);
        });

        b.Entity<OwnedItem>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(50);
            e.HasIndex(x => new { x.UserId, x.Kind, x.Key }).IsUnique();
        });

        b.Entity<UnopenedBox>(e =>
        {
            e.Property(x => x.BoxKey).HasMaxLength(20);
            e.HasIndex(x => x.UserId);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<Conversation>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasMany(x => x.Members).WithOne(x => x.Conversation).HasForeignKey(x => x.ConversationId);
        });

        b.Entity<ConversationMember>(e =>
        {
            e.HasKey(x => new { x.ConversationId, x.UserId });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<Server>(e => e.Property(x => x.Name).HasMaxLength(100));

        b.Entity<Channel>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(500);
            e.HasOne(x => x.Server).WithMany(x => x.Channels).HasForeignKey(x => x.ServerId);
            e.HasMany(x => x.VisibleRoles).WithOne().HasForeignKey(x => x.ChannelId);
            e.HasMany(x => x.Permissions).WithOne().HasForeignKey(x => x.ChannelId);
        });

        b.Entity<ChannelVisibleRole>(e =>
        {
            e.HasKey(x => new { x.ChannelId, x.RoleId });
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
        });

        b.Entity<ChannelPermission>(e =>
        {
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId);
            e.HasIndex(x => new { x.ChannelId, x.RoleId }).IsUnique().HasFilter(null);
        });

        b.Entity<Message>(e =>
        {
            e.Property(x => x.Content).HasMaxLength(4000);
            e.HasOne(x => x.Sender).WithMany().HasForeignKey(x => x.SenderId);
            e.HasOne(x => x.ReplyTo).WithMany().HasForeignKey(x => x.ReplyToId);
            e.HasOne<Conversation>().WithMany().HasForeignKey(x => x.ConversationId);
            e.HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId);
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(x => x.MessageId);
            e.HasMany(x => x.Reactions).WithOne().HasForeignKey(x => x.MessageId);
            e.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            e.HasIndex(x => new { x.ChannelId, x.CreatedAt });
            e.Property(x => x.Embedding).HasColumnType($"vector({TalkieConstants.EmbeddingDimensions})");
        });

        b.Entity<Attachment>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(260);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UploaderId);
        });

        b.Entity<Reaction>(e =>
        {
            e.Property(x => x.Emoji).HasMaxLength(16);
            e.HasKey(x => new { x.MessageId, x.UserId, x.Emoji });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<Report>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.HasOne(x => x.Reporter).WithMany().HasForeignKey(x => x.ReporterId);
            e.HasOne(x => x.ReportedUser).WithMany().HasForeignKey(x => x.ReportedUserId);
        });

        b.Entity<AiMessage>(e =>
        {
            e.Property(x => x.Role).HasMaxLength(20);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId);
        });

        b.Entity<AppSettings>(e =>
        {
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.AppName).HasMaxLength(100);
            e.Property(x => x.Announcement).HasMaxLength(500);
        });

        // SQL Server rejects multiple cascade paths, so deletes are explicit (see DeletionService in the API).
        foreach (var fk in b.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
        {
            fk.DeleteBehavior = DeleteBehavior.NoAction;
        }
    }
}
