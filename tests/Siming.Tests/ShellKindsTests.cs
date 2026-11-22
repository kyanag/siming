using Siming.Core.Configuration;

namespace Siming.Tests;

public sealed class ShellKindsTests
{
    [Fact]
    public void Preferred_UsesPwsh_WhenInstalled()
    {
        Assert.Equal("pwsh", ShellKinds.Preferred(_ => true));
    }

    [Fact]
    public void Preferred_FallsBackToWindowsPowerShell()
    {
        Assert.Equal("powershell", ShellKinds.Preferred(name => name == "powershell.exe"));
    }

    [Fact]
    public void Preferred_FallsBackToCmd_WhenNoPowerShell()
    {
        Assert.Equal("cmd", ShellKinds.Preferred(_ => false));
    }

    [Theory]
    [InlineData("pwsh")]
    [InlineData("powershell")]
    [InlineData("cmd")]
    [InlineData("none")]
    public void Parse_RecognizesOrderedOptions(string shell)
    {
        Assert.True(ShellKinds.IsKnown(shell));
        Assert.Equal(shell, ShellKinds.Parse(shell).ToString().ToLowerInvariant());
    }
}
