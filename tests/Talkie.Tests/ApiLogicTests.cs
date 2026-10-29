using Talkie.Api.Infrastructure;
using Talkie.Api.Modules;
using Talkie.Api.Modules.Chat;
using Talkie.Data;

namespace Talkie.Tests;

public class AfkTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void One_shard_per_full_minute()
    {
        var shards = AfkService.CalculateShards(Start, Start, Start.AddMinutes(5).AddSeconds(59), 1, 1, 24, out var minutes);
        Assert.Equal(5, minutes);
        Assert.Equal(5, shards);
    }

    [Theory]
    [InlineData(2, 20)]
    [InlineData(4, 40)]
    public void Boosters_multiply_shards(int multiplier, long expected) =>
        Assert.Equal(expected, AfkService.CalculateShards(Start, Start, Start.AddMinutes(10), 1, multiplier, 24, out _));

    [Fact]
    public void Earnings_stop_at_the_afk_limit()
    {
        var shards = AfkService.CalculateShards(Start, Start, Start.AddHours(30), 1, 1, 24, out _);
        Assert.Equal(24 * 60, shards);
    }

    [Fact]
    public void Already_credited_minutes_are_not_paid_twice()
    {
        var shards = AfkService.CalculateShards(Start, Start.AddMinutes(3), Start.AddMinutes(4).AddSeconds(30), 1, 1, 24, out _);
        Assert.Equal(1, shards);
    }
}

public class EmbeddingTests
{
    private static float Cosine(float[] a, float[] b) => a.Zip(b, (x, y) => x * y).Sum();

    [Fact]
    public void Vectors_are_normalised_and_have_the_column_dimension()
    {
        var v = HashingEmbeddingGenerator.Embed("Hello team, lunch at noon?");
        Assert.Equal(Talkie.Shared.TalkieConstants.EmbeddingDimensions, v.Length);
        Assert.Equal(1f, MathF.Sqrt(v.Sum(x => x * x)), 3);
    }

    [Fact]
    public void Similar_texts_are_closer_than_unrelated_texts()
    {
        var query = HashingEmbeddingGenerator.Embed("lunch plans");
        var related = HashingEmbeddingGenerator.Embed("What are the plans for lunch today?");
        var unrelated = HashingEmbeddingGenerator.Embed("The deployment pipeline failed on the database migration");
        Assert.True(Cosine(query, related) > Cosine(query, unrelated));
    }

    [Fact]
    public void Empty_text_still_produces_a_valid_vector() =>
        Assert.Contains(HashingEmbeddingGenerator.Embed("  "), x => x != 0);
}

public class ValidationTests
{
    [Theory]
    [InlineData("owner@talkie.com", true)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    public void Email_validation(string email, bool valid) => Assert.Equal(valid, Validation.IsValidEmail(email));

    [Theory]
    [InlineData("fwkh837h@m1", null)]
    [InlineData("short1", "Password must be at least 8 characters.")]
    [InlineData("onlyletters", "Password must contain a letter and a digit.")]
    public void Password_rules(string password, string? problem) => Assert.Equal(problem, Validation.PasswordProblem(password));

    [Theory]
    [InlineData("#90CAF9", true)]
    [InlineData("90CAF9", false)]
    [InlineData("#FFF", false)]
    public void Role_colors_are_hex(string color, bool valid) => Assert.Equal(valid, Validation.IsValidColor(color));

    [Theory]
    [InlineData("Endpoint=smtp://localhost:1025", "localhost", 1025)]
    [InlineData("smtp://mail:25", "mail", 25)]
    public void MailPit_connection_strings_are_parsed(string cs, string host, int port)
    {
        var uri = EmailSender.ParseEndpoint(cs)!;
        Assert.Equal(host, uri.Host);
        Assert.Equal(port, uri.Port);
    }

    [Fact]
    public void Channel_names_are_normalised() => Assert.Equal("team-updates", ServersModule.NormalizeName("  Team   Updates "));
}

public class ChannelPermissionTests
{
    private static readonly Guid CompanyId = Guid.NewGuid();
    private static readonly Guid Moderators = Guid.NewGuid();
    private static readonly Guid Staff = Guid.NewGuid();

    private static Channel NewChannel(params Guid[] visibleTo)
    {
        var channel = TalkieSeeder.CreateChannel("general", null, 0);
        channel.Server = new Server { Name = "Acme", CompanyId = CompanyId };
        channel.VisibleRoles = visibleTo.Select(r => new ChannelVisibleRole { RoleId = r }).ToList();
        channel.Permissions.Add(new ChannelPermission { RoleId = Moderators, CanDeleteMessages = true });
        return channel;
    }

    private static User Member(bool owner = false) => new()
    {
        Email = owner ? "boss@acme.com" : "jane@acme.com", Username = "u", DisplayName = "U", CompanyId = CompanyId, IsOwner = owner,
    };

    [Fact]
    public void By_default_everyone_can_send_and_react_but_not_delete()
    {
        var access = ChatService.Evaluate(NewChannel(), Member(), [])!;
        Assert.True(access.CanSend);
        Assert.True(access.CanReact);
        Assert.False(access.CanDelete);
    }

    [Fact]
    public void Role_permissions_add_to_everyone()
    {
        var access = ChatService.Evaluate(NewChannel(), Member(), [Moderators])!;
        Assert.True(access.CanDelete);
    }

    [Fact]
    public void Role_restricted_channels_are_hidden_from_other_members()
    {
        Assert.Null(ChatService.Evaluate(NewChannel(Staff), Member(), [Moderators]));
        Assert.NotNull(ChatService.Evaluate(NewChannel(Staff), Member(), [Staff]));
    }

    [Fact]
    public void The_company_owner_can_do_everything()
    {
        var access = ChatService.Evaluate(NewChannel(Staff), Member(owner: true), [])!;
        Assert.True(access.CanDelete && access.CanSend && access.CanReact);
    }
}
