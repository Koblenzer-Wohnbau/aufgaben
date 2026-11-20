using Aufgaben.Api.Data;

namespace Aufgaben.Api.Contracts;

public sealed record UserRef(int Id, string Name, string? Email, string? JobTitle)
{
    public static UserRef From(User u) => new(u.Id, u.DisplayName, u.Email, u.JobTitle);
}

public sealed record ProjectDto(int Id, string Label, string? Number, string Name, string? TeamName, bool Archived, bool LinkedToTeams, int OpenCount)
{
    public static ProjectDto From(Project p, int openCount = 0) =>
        new(p.Id, p.Label, p.Number, p.Name, p.TeamName, p.Archived, !string.IsNullOrEmpty(p.TeamsChannelId), openCount);
}

public sealed record ProjectRef(int Id, string Label)
{
    public static ProjectRef? From(Project? p) => p is null ? null : new(p.Id, p.Label);
}

public sealed record ChecklistDto(int Id, string Title, bool Done);

public sealed record TaskDto(
    int Id,
    string Title,
    string Notes,
    string Status,
    string Kind,
    string Priority,
    UserRef CreatedBy,
    UserRef Assignee,
    int? ListId,
    ProjectRef? Project,
    string? DueDate,
    string? DueTime,
    DateTimeOffset? RemindAt,
    bool MyDay,
    string Recurrence,
    DateTimeOffset? CompletedAt,
    UserRef? CompletedBy,
    List<ChecklistDto> Checklist,
    string Source,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int Version,
    bool IsDelegated,
    bool IsOverdue,
    bool CanDelete,
    List<AttachmentDto> Attachments);

public sealed record AttachmentDto(int Id, string FileName, string ContentType, long Size);

public sealed record EventDto(long Id, string Kind, string Text, UserRef? Actor, DateTimeOffset CreatedAt);

/// <summary>CanEdit: Änderungen erlaubt. ViaGrant: nur über ein Einsichtsrecht sichtbar.</summary>
public sealed record TaskDetailDto(TaskDto Task, List<EventDto> Events, bool CanEdit, bool ViaGrant);

public sealed record CreateTaskRequest(
    string Title,
    string? Notes,
    string? Priority,
    string? Kind,
    int? AssigneeId,
    int? ListId,
    int? ProjectId,
    string? DueDate,
    string? DueTime,
    DateTimeOffset? RemindAt,
    bool? MyDay,
    string? Recurrence,
    List<string>? Checklist);

public sealed record ChecklistRequest(string? Title, bool? Done);

public sealed record CommentRequest(string Text);

public sealed record CountsDto(
    int MyDay,
    int Important,
    int Planned,
    int Assigned,
    int Delegated,
    int FollowUps,
    int Overdue,
    Dictionary<int, int> Lists,
    Dictionary<int, int> Projects,
    int Inbox);

public sealed record SuggestionsDto(List<TaskDto> Yesterday, List<TaskDto> DueSoon, List<TaskDto> Important, List<TaskDto> Recent);

public sealed record ListDto(int Id, string Name, string Color, int Position, int OpenCount, bool IsOwner, UserRef Owner, List<UserRef> Members);

public sealed record ListMemberRequest(int UserId);

public sealed record AccessGrantRequest(int OwnerId, int ViewerId, string Permission, string? ValidFrom, string? ValidUntil, string? Purpose);

public sealed record AccessDecisionRequest(string? Note);

public sealed record AccessGrantDto(
    int Id,
    UserRef Owner,
    UserRef Viewer,
    string Permission,
    string ValidFrom,
    string? ValidUntil,
    string Purpose,
    string Status,
    bool IsCurrentlyActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    string ConsentNote,
    DateTimeOffset? RevokedAt,
    string RevokeReason,
    int AccessCount);

public sealed record AccessLogDto(long Id, string Action, UserRef Viewer, UserRef Owner, int? TaskId, DateTimeOffset At);

public sealed record MyAccessDto(int GrantId, UserRef Owner, string Permission, string? ValidUntil);

public sealed record AccessAboutMeDto(List<AccessGrantDto> Grants, List<AccessLogDto> Log);

public sealed record ListRequest(string? Name, string? Color, int? Position);

public sealed record ProjectRequest(string? Number, string? Name, string? TeamName, bool? Archived);

public sealed record ChannelLinkRequest(string ChannelId, string? TeamId, string? TeamName, string? ChannelName, int? ProjectId);

public sealed record MeDto(
    int Id,
    string Name,
    string? Email,
    bool IsAdmin,
    string NotifyChannel,
    bool DigestEnabled,
    bool TeamsConnected,
    bool HasCalendarFeed,
    bool MailAvailable,
    bool TeamsNotificationsAvailable,
    string Today,
    string? MailInboxAddress,
    bool DigestOnWeekends);

public sealed record MeRequest(string? NotifyChannel, bool? DigestEnabled);

public sealed record CalendarFeedDto(string Url);

public sealed record TemplateStepDto(int Id, int Position, string Title, string Notes, int OffsetDays, string Priority, List<string> Checklist, string RoleHint);

public sealed record TemplateDto(int Id, string Name, string Description, List<TemplateStepDto> Steps);

public sealed record TemplateStepRequest(string Title, string? Notes, int OffsetDays, string? Priority, List<string>? Checklist, string? RoleHint);

public sealed record TemplateRequest(string Name, string? Description, List<TemplateStepRequest> Steps);

public sealed record ApplyTemplateRequest(string StartDate, int? ProjectId, int? DefaultAssigneeId, Dictionary<int, int>? Assignees, string? TitleSuffix);

public sealed record ApplyTemplateResult(int Created, List<int> TaskIds);

public sealed record AdminStatusDto(
    bool GraphConfigured,
    bool MailConfigured,
    bool TeamsNotificationsConfigured,
    int ActiveUsers,
    int PendingNotifications,
    int FailedNotifications,
    DateTimeOffset? LastDirectorySync,
    string? LastDirectorySyncResult,
    bool MailInboxConfigured,
    DateTimeOffset? LastMailInboxRun,
    string? LastMailInboxResult,
    int RejectedMailsLast7Days,
    string Version);
