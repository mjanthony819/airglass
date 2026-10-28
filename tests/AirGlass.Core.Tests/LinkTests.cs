using Xunit;
using AirGlass.Core.Link;

namespace AirGlass.Core.Tests;

public class PairingTests
{
    private static (PairingService s, Func<DateTimeOffset> now, Action<TimeSpan> adv) Make()
    {
        var t = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        return (new PairingService(() => t), () => t, d => t += d);
    }

    [Fact]
    public void Full_flow_needs_code_and_pc_approval_and_token_is_handed_out_once()
    {
        var (s, _, _) = Make();
        var code = s.NewCode();
        Assert.Matches("^[0-9]{6}$", code);
        Assert.Equal(PairResult.Pending, s.Submit(code, "My iPhone", out var dev));
        Assert.Equal(("pending", (string?)null), s.Poll(dev!.Id));
        Assert.Null(s.Validate("anything"));
        Assert.True(s.Approve(dev.Id));
        var (st, token) = s.Poll(dev.Id);
        Assert.Equal("approved", st);
        Assert.Equal("My iPhone", s.Validate(token));
        Assert.Equal("denied", s.Poll(dev.Id).Status);
        Assert.Equal(1, s.PairedCount);
    }

    [Fact]
    public void Code_is_single_use()
    {
        var (s, _, _) = Make();
        var code = s.NewCode();
        Assert.Equal(PairResult.Pending, s.Submit(code, "a", out _));
        Assert.Equal(PairResult.Expired, s.Submit(code, "b", out _));
    }

    [Fact]
    public void Code_expires()
    {
        var (s, _, adv) = Make();
        var code = s.NewCode();
        adv(TimeSpan.FromMinutes(3));
        Assert.Equal(PairResult.Expired, s.Submit(code, "a", out _));
    }

    [Fact]
    public void Wrong_guesses_burn_the_code_and_lock_out()
    {
        var (s, _, adv) = Make();
        var code = s.NewCode();
        var wrong = code == "000000" ? "111111" : "000000";
        for (var i = 0; i < 4; i++) Assert.Equal(PairResult.BadCode, s.Submit(wrong, "a", out _));
        Assert.Equal(PairResult.LockedOut, s.Submit(wrong, "a", out _));
        Assert.Equal(PairResult.LockedOut, s.Submit(code, "a", out _));
        adv(TimeSpan.FromMinutes(2));
        Assert.Equal(PairResult.Expired, s.Submit(code, "a", out _)); // burned
    }

    [Fact]
    public void Deny_and_revoke_remove_access()
    {
        var (s, _, _) = Make();
        s.Submit(s.NewCode() is var c ? c : "", "x", out var dev);
        Assert.True(s.Deny(dev!.Id));
        Assert.False(s.Approve(dev.Id));
        s.Submit(s.NewCode() is var c2 ? c2 : "", "y", out var d2);
        s.Approve(d2!.Id);
        var token = s.Poll(d2.Id).Token;
        s.RevokeAll();
        Assert.Null(s.Validate(token));
    }

    [Fact]
    public void Device_name_is_cleaned()
    {
        var (s, _, _) = Make();
        s.Submit(s.NewCode() is var c ? c : "", "ev\nil\u0007" + new string('a', 100), out var dev);
        Assert.DoesNotContain('\n', dev!.DeviceName);
        Assert.True(dev.DeviceName.Length <= 40);
    }

    [Fact]
    public void Garbage_tokens_are_rejected()
    {
        var (s, _, _) = Make();
        Assert.Null(s.Validate(null));
        Assert.Null(s.Validate(""));
        Assert.Null(s.Validate(new string('x', 500)));
    }
}

public class SafeFilesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ag-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

    [Theory]
    [InlineData("../../Windows/evil.txt", "evil.txt")]
    [InlineData("C:\\x\\y\\photo.jpg", "photo.jpg")]
    [InlineData("", "file")]
    [InlineData("...", "file")]
    [InlineData("a:b*c.txt", "a_b_c.txt")]
    [InlineData("CON.txt", "_CON.txt")]
    public void CleanName_strips_paths_and_bad_chars(string raw, string expected) =>
        Assert.Equal(expected, SafeFiles.CleanName(raw));

    [Fact]
    public void Long_names_are_shortened_keeping_extension() =>
        Assert.EndsWith(".png", SafeFiles.CleanName(new string('a', 300) + ".png"));

    [Fact]
    public void Executables_get_blocked_suffix()
    {
        var p = SafeFiles.UniquePath(_dir, "setup.exe");
        Assert.EndsWith("setup.exe.blocked", p);
    }

    [Fact]
    public void Never_overwrites_existing_files()
    {
        var a = SafeFiles.UniquePath(_dir, "a.txt");
        File.WriteAllText(a, "1");
        var b = SafeFiles.UniquePath(_dir, "a.txt");
        Assert.NotEqual(a, b);
        Assert.EndsWith("a (1).txt", b);
    }

    [Fact]
    public void Traversal_names_stay_inside_inbox()
    {
        var p = SafeFiles.UniquePath(_dir, "..\\..\\x.txt");
        Assert.True(SafeFiles.IsInside(_dir, p));
    }

    [Fact]
    public void ResolveShared_rejects_traversal_and_missing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "ok.txt"), "x");
        Assert.NotNull(SafeFiles.ResolveShared(_dir, "ok.txt"));
        Assert.Null(SafeFiles.ResolveShared(_dir, "../ok.txt"));
        Assert.Null(SafeFiles.ResolveShared(_dir, "sub/ok.txt"));
        Assert.Null(SafeFiles.ResolveShared(_dir, "nope.txt"));
        Assert.Null(SafeFiles.ResolveShared(_dir, null));
    }
}

public class InputParserTests
{
    [Fact]
    public void Move_is_clamped()
    {
        Assert.True(InputParser.TryParse("{\"t\":\"move\",\"dx\":99999,\"dy\":-5}", out var m));
        Assert.Equal(InputParser.MaxMove, m!.A);
        Assert.Equal(-5, m.B);
    }

    [Theory]
    [InlineData("{\"t\":\"click\",\"b\":\"left\"}")]
    [InlineData("{\"t\":\"click\",\"b\":\"right\"}")]
    [InlineData("{\"t\":\"scroll\",\"dy\":3}")]
    [InlineData("{\"t\":\"text\",\"s\":\"hello\\nworld\"}")]
    [InlineData("{\"t\":\"key\",\"k\":\"enter\"}")]
    public void Valid_messages_parse(string json) => Assert.True(InputParser.TryParse(json, out _));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"t\":\"move\",\"dx\":\"1\",\"dy\":2}")]
    [InlineData("{\"t\":\"move\",\"dx\":1.5,\"dy\":2}")]
    [InlineData("{\"t\":\"click\",\"b\":\"middle\"}")]
    [InlineData("{\"t\":\"key\",\"k\":\"win\"}")]
    [InlineData("{\"t\":\"key\",\"k\":\"ctrl+alt+delete\"}")]
    [InlineData("{\"t\":\"text\",\"s\":\"\"}")]
    [InlineData("{\"t\":\"text\",\"s\":\"a\\u0007b\"}")]
    [InlineData("{\"t\":\"shell\",\"cmd\":\"calc\"}")]
    public void Invalid_messages_are_rejected(string json) => Assert.False(InputParser.TryParse(json, out _));

    [Fact]
    public void Oversized_text_is_rejected() =>
        Assert.False(InputParser.TryParse("{\"t\":\"text\",\"s\":\"" + new string('a', 600) + "\"}", out _));
}
