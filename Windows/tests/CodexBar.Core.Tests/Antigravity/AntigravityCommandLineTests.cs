using CodexBar.Core.Providers.Antigravity;
using Xunit;

namespace CodexBar.Core.Tests.Antigravity;

public sealed class AntigravityCommandLineTests
{
    [Fact]
    public void Classifies_the_app_language_server_and_reads_its_flags()
    {
        var process = new AntigravityProcessInfo(
            4242,
            "\"C:\\Users\\Test\\AppData\\Local\\Programs\\Antigravity\\resources\\app\\language_server.exe\" "
            + "--app_data_dir antigravity --csrf_token token-123 --extension_server_port 51234");

        var endpoint = AntigravityCommandLine.Parse(process);

        Assert.NotNull(endpoint);
        Assert.Equal(4242, endpoint.Pid);
        Assert.Equal(AntigravityProcessKind.App, endpoint.Kind);
        Assert.Equal("token-123", endpoint.CsrfToken);
        Assert.Equal(51234, endpoint.ExtensionServerPort);
    }

    [Fact]
    public void Classifies_the_ide_language_server()
    {
        var process = new AntigravityProcessInfo(
            77,
            "C:\\Users\\Test\\.antigravity\\extensions\\antigravity\\bin\\language_server.exe "
            + "--csrf_token ide-token --extension_server_port=5000");

        var endpoint = AntigravityCommandLine.Parse(process);

        Assert.Equal(AntigravityProcessKind.Ide, endpoint!.Kind);
        Assert.Equal("ide-token", endpoint.CsrfToken);
        Assert.Equal(5000, endpoint.ExtensionServerPort);
    }

    [Fact]
    public void Classifies_the_cli_without_a_csrf_token()
    {
        var process = new AntigravityProcessInfo(
            9,
            "C:\\Users\\Test\\AppData\\Local\\Programs\\agy\\agy.exe --session local");

        var endpoint = AntigravityCommandLine.Parse(process);

        Assert.Equal(AntigravityProcessKind.Cli, endpoint!.Kind);
        Assert.Null(endpoint.CsrfToken);
    }

    [Fact]
    public void Leaves_a_tokenless_ide_server_for_the_provider_to_reject()
    {
        var process = new AntigravityProcessInfo(
            12,
            "C:\\Antigravity\\extensions\\antigravity\\bin\\language_server.exe --app_data_dir antigravity-ide");

        var endpoint = AntigravityCommandLine.Parse(process);

        Assert.Equal(AntigravityProcessKind.Ide, endpoint!.Kind);
        Assert.Null(endpoint.CsrfToken);
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\notepad.exe")]
    [InlineData("C:\\Program Files\\Google\\Chrome\\chrome.exe --type=renderer")]
    [InlineData("")]
    public void Ignores_unrelated_processes(string commandLine)
    {
        Assert.Null(AntigravityCommandLine.Parse(new AntigravityProcessInfo(1, commandLine)));
    }
}
