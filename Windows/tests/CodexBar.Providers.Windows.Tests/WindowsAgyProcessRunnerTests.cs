using System.Diagnostics;
using CodexBar.Providers.Windows;
using Xunit;

namespace CodexBar.Providers.Windows.Tests;

public sealed class WindowsAgyProcessRunnerTests
{
    [Fact]
    public async Task A_missing_binary_reports_not_installed_without_starting_anything()
    {
        var runner = new WindowsAgyProcessRunner(() => null);

        var result = await runner.RunAsync(["--version"], TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.False(result.Started);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task An_unusable_binary_reports_not_installed_instead_of_throwing()
    {
        var runner = new WindowsAgyProcessRunner(() => @"C:\does-not-exist\agy.exe");

        var result = await runner.RunAsync(["--version"], TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.False(result.Started);
    }

    [Fact]
    public async Task Output_and_exit_code_are_captured_from_a_hidden_process()
    {
        var runner = new WindowsAgyProcessRunner(() => CommandInterpreter());

        var result = await runner.RunAsync(
            ["/c", "echo 1.4.8"],
            TimeSpan.FromSeconds(20),
            CancellationToken.None);

        Assert.True(result.Started);
        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1.4.8", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task A_process_that_overruns_is_killed_and_reported_as_a_timeout()
    {
        var runner = new WindowsAgyProcessRunner(() => CommandInterpreter());

        var result = await runner.RunAsync(
            ["/c", "ping -n 60 127.0.0.1"],
            TimeSpan.FromMilliseconds(300),
            CancellationToken.None);

        Assert.True(result.Started);
        Assert.True(result.TimedOut);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var runner = new WindowsAgyProcessRunner(() => CommandInterpreter());
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => runner.RunAsync(["/c", "ping -n 60 127.0.0.1"], TimeSpan.FromSeconds(30), source.Token));
    }

    private static string CommandInterpreter()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        return System.IO.Path.Combine(windows, "System32", "cmd.exe");
    }
}
