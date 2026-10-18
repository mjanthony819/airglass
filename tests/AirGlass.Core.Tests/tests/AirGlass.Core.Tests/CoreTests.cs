using Xunit;
using AirGlass.Core;

namespace AirGlass.Core.Tests;

public class ArgumentTests
{
    [Fact]
    public void Defaults_build_a_valid_command_line()
    {
        var args = UxPlayArguments.Build(new AppSettings { DeviceName = "Anthony PC" });
        Assert.Equal(new[] { "-n", "Anthony PC", "-nh", "-s", "1920x1080@60", "-p", "7000" }, args);
    }

    [Fact]
    public void Low_latency_disables_vsync()
    {
        var args = UxPlayArguments.Build(new AppSettings { LowLatency = true });
        Assert.Contains("-vsync", args);
        Assert.Contains("no", args);
    }

    [Fact]
    public void Fixed_pin_is_a_single_argument()
    {
        var args = UxPlayArguments.Build(new AppSettings { UsePin = true, Pin = "4321" });
        Assert.Contains("-pin4321", args);
    }

    [Fact]
    public void Empty_pin_with_pin_enabled_asks_for_random_pin()
    {
        var args = UxPlayArguments.Build(new AppSettings { UsePin = true, Pin = "" });
        Assert.Contains("-pin", args);
        Assert.DoesNotContain(args, a => a.StartsWith("-pin") && a.Length > 4);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("abcd")]
    public void Bad_pins_are_rejected(string pin)
    {
        Assert.Throws<ArgumentException>(() =>
            UxPlayArguments.Build(new AppSettings { UsePin = true, Pin = pin }));
    }

    [Theory]
    [InlineData(1023)]
    [InlineData(65533)]
    public void Bad_ports_are_rejected(int port)
    {
        Assert.Throws<ArgumentException>(() =>
            UxPlayArguments.Build(new AppSettings { BasePort = port }));
    }

    [Fact]
    public void Unknown_resolution_is_rejected()
    {
        Assert.Throws<ArgumentException>(() =>
            UxPlayArguments.Build(new AppSettings { Resolution = "999x999" }));
    }
}

public class StatusParserTests
{
    [Fact]
    public void Full_connection_lifecycle()
    {
        var p = new StatusParser();
        Assert.Equal(MirrorStatus.Starting, p.Status);

        Assert.True(p.Feed("Initialized server socket(s)"));
        Assert.Equal(MirrorStatus.Waiting, p.Status);

        Assert.True(p.Feed("Accepted Mirror data client on socket 12, port 7001"));
        Assert.Equal(MirrorStatus.Connected, p.Status);

        Assert.True(p.Feed("Remote: 192.168.1.42"));
        Assert.Equal("192.168.1.42", p.ClientAddress);

        Assert.True(p.Feed("registered new client: Anthony's iPhone DeviceID = aa:bb"));
        Assert.Equal("Anthony's iPhone", p.ClientName);

        Assert.True(p.Feed("Removing connection for socket 12"));
        Assert.Equal(MirrorStatus.Waiting, p.Status);
        Assert.Null(p.ClientAddress);
        Assert.Null(p.ClientName);
    }

    [Fact]
    public void Multiple_sockets_do_not_drop_status_early()
    {
        var p = new StatusParser();
        p.Feed("Initialized server socket(s)");
        p.Feed("Accepted AirPlay data client on socket 10, port 7000");
        p.Feed("Accepted Mirror data client on socket 12, port 7001");
        p.Feed("Removing connection for socket 12");
        Assert.Equal(MirrorStatus.Connected, p.Status);
        p.Feed("Removing connection for socket 10");
        Assert.Equal(MirrorStatus.Waiting, p.Status);
    }

    [Fact]
    public void Unrelated_lines_do_not_change_state()
    {
        var p = new StatusParser();
        p.Feed("Initialized server socket(s)");
        Assert.False(p.Feed("some gstreamer warning"));
        Assert.Equal(MirrorStatus.Waiting, p.Status);
    }
}

public class SettingsTests
{
    [Fact]
    public void Settings_round_trip_through_json()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var s = new AppSettings
            {
                DeviceName = "Test Rig",
                Resolution = "1280x720",
                LowLatency = true,
                UsePin = true,
                Pin = "1234",
                BasePort = 7100,
                StartWithWindows = true,
            };
            s.Save(path);
            var loaded = AppSettings.Load(path);
            Assert.Equal("Test Rig", loaded.DeviceName);
            Assert.Equal("1280x720", loaded.Resolution);
            Assert.True(loaded.LowLatency);
            Assert.True(loaded.UsePin);
            Assert.Equal("1234", loaded.Pin);
            Assert.Equal(7100, loaded.BasePort);
            Assert.True(loaded.StartWithWindows);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Corrupt_settings_fall_back_to_defaults()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{ not json !!!");
        var loaded = AppSettings.Load(path);
        Assert.Equal("AirGlass", loaded.DeviceName);
        File.Delete(path);
    }
}

public class ChecksumTests
{
    [Fact]
    public void Sha256_matches_known_value()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".txt");
        File.WriteAllText(path, "airglass");
        // echo -n "airglass" | sha256sum
        Assert.Equal("686be48db39eb5d29754705d4fd608dae1d568087e2e4593c327a4fb9958c2bc",
            EngineInstaller.Sha256OfFile(path));
        File.Delete(path);
    }
}
