using System.Net;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;
using Xunit;
using Microsoft.EntityFrameworkCore;
using MiraiNote.Core.Services.Household;
using MiraiNote.Data.Entities;
using MiraiNote.Shared.Common;

namespace MiraiNote.Tests;

public class HouseholdBarkSecurityTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.20")]
    [InlineData("100.64.0.1")]
    [InlineData("100.127.255.1")]
    [InlineData("169.254.169.254")]
    [InlineData("224.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.8")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:192.168.0.1")]
    public void BlocksNonPublicAddresses(string text)
    {
        Assert.True(HouseholdBarkNetwork.IsBlocked(IPAddress.Parse(text)));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.0.1")]
    [InlineData("100.63.0.1")]
    [InlineData("2606:4700:4700::1111")]
    public void AllowsPublicAddresses(string text)
    {
        Assert.False(HouseholdBarkNetwork.IsBlocked(IPAddress.Parse(text)));
    }

    [Fact]
    public async Task Connect_RejectsDnsThatResolvesToAPrivateAddress()
    {
        var opened = false;
        var blocked = await Assert.ThrowsAsync<HouseholdNotificationDeliveryException>(() =>
            HouseholdBarkConnector.ConnectAsync(
                "rebind.example",
                443,
                (_, _) => Task.FromResult(new[] { IPAddress.Parse("1.1.1.1"), IPAddress.Parse("10.1.2.3") }),
                (_, _, _) =>
                {
                    opened = true;
                    return Task.FromResult<Stream>(Stream.Null);
                },
                CancellationToken.None));

        Assert.False(opened);
        Assert.Equal("Bark 通知发送失败", blocked.Message);
    }

    [Fact]
    public async Task Connect_RejectsLiteralLoopbackWithoutDns()
    {
        var opened = false;
        await Assert.ThrowsAsync<HouseholdNotificationDeliveryException>(() =>
            HouseholdBarkConnector.ConnectAsync(
                "127.0.0.1",
                8088,
                (_, _) => throw new InvalidOperationException("should not resolve"),
                (_, _, _) =>
                {
                    opened = true;
                    return Task.FromResult<Stream>(Stream.Null);
                },
                CancellationToken.None));
        Assert.False(opened);
    }

    [Fact]
    public async Task Connect_UsesTheResolvedPublicAddress()
    {
        IPAddress? connected = null;
        await using var stream = await HouseholdBarkConnector.ConnectAsync(
            "api.day.app",
            443,
            (_, _) => Task.FromResult(new[] { IPAddress.Parse("1.1.1.1") }),
            (address, port, _) =>
            {
                connected = address;
                Assert.Equal(443, port);
                return Task.FromResult<Stream>(new MemoryStream());
            },
            CancellationToken.None);

        Assert.Equal(IPAddress.Parse("1.1.1.1"), connected);
    }

    [Fact]
    public void AllowList_AlwaysIncludesDayApp_AndAppendsConfiguredHosts()
    {
        var hosts = HouseholdBarkAddresses.AllowedHosts(new HouseholdNotificationOptions
        {
            BarkAllowedHosts = [" Bark.Example.Test. ", "https://self.example/ignore"]
        });
        Assert.Contains("api.day.app", hosts);
        Assert.Contains("bark.example.test", hosts);
        Assert.Contains("self.example", hosts);
    }

    [Theory]
    [InlineData("http://api.day.app/key", HouseholdBarkAddresses.HttpsOnlyMessage)]
    [InlineData("http:evil.com", HouseholdBarkAddresses.HttpsOnlyMessage)]
    [InlineData("javascript:alert(1)", HouseholdBarkAddresses.HttpsOnlyMessage)]
    [InlineData("https://127.0.0.1/key", HouseholdBarkAddresses.UnusableMessage)]
    [InlineData("https://api.day.app.evil.com/key", HouseholdBarkAddresses.HostRejectedMessage)]
    [InlineData("https://user:secret@api.day.app/key", HouseholdBarkAddresses.UnusableMessage)]
    [InlineData("https://api.day.app:8443/key", HouseholdBarkAddresses.PortRejectedMessage)]
    [InlineData("https://api.day.app:80/key", HouseholdBarkAddresses.PortRejectedMessage)]
    [InlineData("https://api.day.app:4430/key", HouseholdBarkAddresses.PortRejectedMessage)]
    public void Require_RejectsUnsafeBarkAddresses(string value, string message)
    {
        var ex = Assert.Throws<BusinessException>(() =>
            HouseholdBarkAddresses.Require(value, new HouseholdNotificationOptions(), 500));
        Assert.Equal(400, ex.StatusCode);
        Assert.Equal(message, ex.Message);
        Assert.DoesNotContain("secret", ex.Message);
    }

    [Fact]
    public void Require_AcceptsDayAppAndConfiguredHosts()
    {
        var options = new HouseholdNotificationOptions { BarkAllowedHosts = ["bark.example.test"] };
        Assert.Equal(
            "https://api.day.app/device-key",
            HouseholdBarkAddresses.Require("https://api.day.app/device-key", options, 500));
        Assert.Equal(
            "https://api.day.app/device-key",
            HouseholdBarkAddresses.Require("https://api.day.app:443/device-key", options, 500));
        Assert.EndsWith(
            "bark.example.test/device",
            HouseholdBarkAddresses.Require("https://bark.example.test/device", options, 500));
    }

    [Fact]
    public void UniqueConflict_RecognizesSqlServerNumbersAndSqliteUnique()
    {
        Assert.True(HouseholdUniqueConflict.IsSqlServerUniqueNumber(2601));
        Assert.True(HouseholdUniqueConflict.IsSqlServerUniqueNumber(2627));
        Assert.False(HouseholdUniqueConflict.IsSqlServerUniqueNumber(547));

        var sqlite = new SqliteException("UNIQUE constraint failed: HouseholdReminderLog", 2067);
        Assert.True(HouseholdUniqueConflict.IsExpected(new DbUpdateException("conflict", sqlite)));
        Assert.False(HouseholdUniqueConflict.IsExpected(new DbUpdateException("other", new InvalidOperationException("fk"))));
    }

    [Fact]
    public void Attempt_RetriesFailuresWithBackoff_AndLetsSkippedSendAgain()
    {
        var at = new DateTime(2026, 10, 8, 1, 0, 0, DateTimeKind.Utc);
        Assert.False(HouseholdReminderAttempt.CanClaim(HouseholdReminderDeliveryStatus.Failed, 1, at, at.AddSeconds(30)));
        Assert.True(HouseholdReminderAttempt.CanClaim(HouseholdReminderDeliveryStatus.Failed, 1, at, at.AddMinutes(1)));
        Assert.False(HouseholdReminderAttempt.CanClaim(HouseholdReminderDeliveryStatus.Failed, 2, at, at.AddMinutes(4)));
        Assert.True(HouseholdReminderAttempt.CanClaim(HouseholdReminderDeliveryStatus.Failed, 2, at, at.AddMinutes(5)));
        Assert.False(HouseholdReminderAttempt.CanClaim(HouseholdReminderDeliveryStatus.Failed, 3, at, at.AddHours(2)));
        Assert.False(HouseholdReminderAttempt.CanClaim(HouseholdReminderDeliveryStatus.Pending, 1, at, at.AddSeconds(30)));
        Assert.True(HouseholdReminderAttempt.CanClaim(HouseholdReminderDeliveryStatus.Pending, 1, at, at.AddSeconds(60)));
        Assert.True(HouseholdReminderAttempt.CanClaim(HouseholdReminderDeliveryStatus.Skipped, 3, at, at));
        Assert.False(HouseholdReminderAttempt.CanClaim(HouseholdReminderDeliveryStatus.Sent, 1, at, at.AddDays(1)));
        Assert.Equal("发送失败（InvalidOperationException）", HouseholdReminderAttempt.Summarize(new InvalidOperationException("https://api.day.app/secret")));
    }
}
