using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Aufgaben.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<TaskList> TaskLists => Set<TaskList>();
    public DbSet<TaskListMember> TaskListMembers => Set<TaskListMember>();
    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();
    public DbSet<TaskEvent> TaskEvents => Set<TaskEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<TemplateStep> TemplateSteps => Set<TemplateStep>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();
    public DbSet<AttachmentBlob> AttachmentBlobs => Set<AttachmentBlob>();
    public DbSet<InboundMail> InboundMails => Set<InboundMail>();
    public DbSet<AccessGrant> AccessGrants => Set<AccessGrant>();
    public DbSet<AccessLogEntry> AccessLog => Set<AccessLogEntry>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(x => x.EntraOid).IsUnique();
            e.HasIndex(x => x.Email);
            e.HasIndex(x => x.IcsTokenHash).IsUnique();
            e.Property(x => x.EntraOid).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Upn).HasMaxLength(320);
            e.Property(x => x.JobTitle).HasMaxLength(200);
            e.Property(x => x.NotifyChannel).HasMaxLength(16);
            e.Property(x => x.IcsTokenHash).HasMaxLength(64);
        });

        b.Entity<Project>(e =>
        {
            e.ToTable("projects");
            e.HasIndex(x => x.TeamsChannelId).IsUnique();
            e.HasIndex(x => x.Number);
            e.Property(x => x.Number).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.TeamName).HasMaxLength(200);
            e.Property(x => x.TeamsTeamId).HasMaxLength(100);
            e.Property(x => x.TeamsChannelId).HasMaxLength(200);
            e.Ignore(x => x.Label);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<TaskList>(e =>
        {
            e.ToTable("task_lists");
            e.HasIndex(x => x.OwnerId);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Color).HasMaxLength(16);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Members).WithOne().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TaskListMember>(e =>
        {
            e.ToTable("task_list_members");
            e.HasKey(x => new { x.ListId, x.UserId });
            e.HasIndex(x => x.UserId);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TaskItem>(e =>
        {
            e.ToTable("tasks");
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.Priority).HasMaxLength(16);
            e.Property(x => x.Recurrence).HasMaxLength(16);
            e.Property(x => x.Source).HasMaxLength(32);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.Ignore(x => x.IsDone);
            e.HasIndex(x => new { x.AssigneeId, x.Status });
            e.HasIndex(x => new { x.CreatedById, x.Status });
            e.HasIndex(x => new { x.ProjectId, x.Status });
            e.HasIndex(x => x.DueDate);
            e.HasIndex(x => x.RemindAt);
            e.HasOne(x => x.CreatedBy).WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CompletedBy).WithMany().HasForeignKey(x => x.CompletedById).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne<TaskList>().WithMany().HasForeignKey(x => x.ListId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Checklist).WithOne().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<TaskAttachment>(e =>
        {
            e.ToTable("task_attachments");
            e.HasIndex(x => x.TaskId);
            e.Property(x => x.FileName).HasMaxLength(255);
            e.Property(x => x.ContentType).HasMaxLength(127);
            e.Property(x => x.Sha256).HasMaxLength(64);
        });

        b.Entity<AttachmentBlob>(e =>
        {
            e.ToTable("attachment_blobs");
            e.HasKey(x => x.AttachmentId);
            e.HasOne<TaskAttachment>().WithOne().HasForeignKey<AttachmentBlob>(x => x.AttachmentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AccessGrant>(e =>
        {
            e.ToTable("access_grants");
            e.HasIndex(x => new { x.ViewerId, x.Status });
            e.HasIndex(x => new { x.OwnerId, x.Status });
            e.Property(x => x.Permission).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Purpose).HasMaxLength(300);
            e.Property(x => x.ConsentNote).HasMaxLength(300);
            e.Property(x => x.RevokeReason).HasMaxLength(300);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Viewer).WithMany().HasForeignKey(x => x.ViewerId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ConfirmedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RevokedById).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AccessLogEntry>(e =>
        {
            e.ToTable("access_log");
            e.HasIndex(x => new { x.GrantId, x.At });
            e.HasIndex(x => new { x.OwnerId, x.At });
            e.Property(x => x.Action).HasMaxLength(16);
            e.HasOne<AccessGrant>().WithMany().HasForeignKey(x => x.GrantId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<InboundMail>(e =>
        {
            e.ToTable("inbound_mails");
            e.HasIndex(x => x.MessageKey).IsUnique();
            e.Property(x => x.MessageKey).HasMaxLength(64);
            e.Property(x => x.FromAddress).HasMaxLength(320);
            e.Property(x => x.Subject).HasMaxLength(500);
            e.Property(x => x.Result).HasMaxLength(16);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<TaskItem>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<ChecklistItem>(e =>
        {
            e.ToTable("checklist_items");
            e.Property(x => x.Title).HasMaxLength(300);
        });

        b.Entity<TaskEvent>(e =>
        {
            e.ToTable("task_events");
            e.HasIndex(x => x.TaskId);
            e.Property(x => x.Kind).HasMaxLength(32);
            e.HasOne<TaskItem>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Notification>(e =>
        {
            e.ToTable("notifications");
            e.HasIndex(x => new { x.SentAt, x.NextAttemptAt });
            e.Property(x => x.Kind).HasMaxLength(32);
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.ActorName).HasMaxLength(200);
            e.Property(x => x.Channel).HasMaxLength(16);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<TaskItem>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Template>(e =>
        {
            e.ToTable("templates");
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasMany(x => x.Steps).WithOne().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<TemplateStep>(e =>
        {
            e.ToTable("template_steps");
            e.Property(x => x.Title).HasMaxLength(500);
            e.Property(x => x.Priority).HasMaxLength(16);
            e.Property(x => x.RoleHint).HasMaxLength(120);
        });

        b.Entity<DataProtectionKey>().ToTable("data_protection_keys");
    }
}
