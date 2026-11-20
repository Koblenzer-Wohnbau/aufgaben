using System.Text;
using Aufgaben.Api.Data;
using Aufgaben.Api.Endpoints;
using Aufgaben.Api.Infrastructure;
using Aufgaben.Api.Services;

namespace Aufgaben.Tests;

public class RecurrenceTests
{
    [Theory]
    [InlineData("2026-11-20", "taeglich", "2026-11-21")]
    [InlineData("2026-11-20", "werktags", "2026-11-23")] // Freitag → Montag
    [InlineData("2026-11-23", "werktags", "2026-11-24")]
    [InlineData("2026-11-20", "woechentlich", "2026-11-27")]
    [InlineData("2026-01-31", "monatlich", "2026-02-28")]
    [InlineData("2024-02-29", "jaehrlich", "2025-02-28")]
    public void Next(string from, string rule, string expected)
    {
        Assert.Equal(DateOnly.Parse(expected), Recurrence.Next(DateOnly.Parse(from), rule));
    }

    [Fact]
    public void NextOnOrAfter_skips_missed_occurrences()
    {
        var next = Recurrence.NextOnOrAfter(new DateOnly(2026, 11, 2), "woechentlich", new DateOnly(2026, 11, 20));
        Assert.Equal(new DateOnly(2026, 11, 23), next);
    }
}

public class CalendarFeedTests
{
    [Fact]
    public void Escape_handles_special_characters()
    {
        Assert.Equal("a\\, b\\; c\\\\ d\\ne", CalendarFeed.Escape("a, b; c\\ d\ne"));
    }

    [Fact]
    public void Long_lines_are_folded_at_75_octets_without_breaking_umlauts()
    {
        var sb = new StringBuilder();
        CalendarFeed.Line(sb, "SUMMARY:" + new string('ü', 80));
        var lines = sb.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1);
        Assert.All(lines, l => Assert.True(Encoding.UTF8.GetByteCount(l) <= 75));
        Assert.All(lines.Skip(1), l => Assert.StartsWith(" ", l));
        var unfolded = string.Concat(lines.Select((l, i) => i == 0 ? l : l[1..]));
        Assert.Equal("SUMMARY:" + new string('ü', 80), unfolded);
    }

    [Fact]
    public void Token_hash_is_stable_and_token_is_random()
    {
        var (a, hashA) = CalendarFeed.NewToken();
        var (b, _) = CalendarFeed.NewToken();
        Assert.NotEqual(a, b);
        Assert.True(a.Length >= 40);
        Assert.Equal(hashA, CalendarFeed.Hash(a));
        Assert.Equal(64, hashA.Length);
    }
}

public class ValidationTests
{
    [Fact]
    public void ParseDate_accepts_iso_only()
    {
        Assert.Equal(new DateOnly(2026, 11, 23), TaskService.ParseDate("2026-11-23"));
        Assert.Null(TaskService.ParseDate(""));
        Assert.Throws<ApiException>(() => TaskService.ParseDate("23.11.2026"));
    }

    [Fact]
    public void ParseTime_accepts_hours_and_minutes()
    {
        Assert.Equal(new TimeOnly(10, 30), TaskService.ParseTime("10:30"));
        Assert.Throws<ApiException>(() => TaskService.ParseTime("25:00"));
    }

    [Fact]
    public void EscapeLike_escapes_wildcards()
    {
        Assert.Equal("100\\% \\_x\\\\", TaskService.EscapeLike("100% _x\\"));
    }

    [Theory]
    [InlineData("1001 Lindenpark", "1001", "Lindenpark")]
    [InlineData("2002 Bergweg 3-7", "2002", "Bergweg 3-7")]
    [InlineData("Quartier Nord", null, "Quartier Nord")]
    [InlineData("00 Neubau", "00", "Neubau")]
    public void Channel_names_are_split_into_number_and_name(string channel, string? number, string name)
    {
        var (n, nm) = AppEndpoints.SplitChannelName(channel);
        Assert.Equal(number, n);
        Assert.Equal(name, nm);
    }
}

public class ChannelResolutionTests
{
    private static User U(string channel, bool teamsSeen, string? email = "x@example.test") => new()
    {
        EntraOid = "x",
        DisplayName = "X",
        NotifyChannel = channel,
        TeamsSeenAt = teamsSeen ? DateTimeOffset.UtcNow : null,
        Email = email,
    };

    [Fact]
    public void Auto_prefers_teams_once_the_app_was_opened_in_teams()
    {
        Assert.Equal("teams", BackgroundWorker.ResolveChannel(U("auto", true), teamsAvailable: true, mailAvailable: true));
    }

    [Fact]
    public void Auto_uses_mail_for_people_without_teams()
    {
        Assert.Equal("email", BackgroundWorker.ResolveChannel(U("auto", false), teamsAvailable: true, mailAvailable: true));
    }

    [Fact]
    public void Off_means_no_notification()
    {
        Assert.Equal("keiner", BackgroundWorker.ResolveChannel(U("aus", true), true, true));
    }

    [Fact]
    public void Without_any_channel_nothing_is_sent()
    {
        Assert.Equal("keiner", BackgroundWorker.ResolveChannel(U("auto", false, email: null), teamsAvailable: false, mailAvailable: true));
    }
}
