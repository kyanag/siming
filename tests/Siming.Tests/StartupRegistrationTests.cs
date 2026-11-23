using Siming.Core.Platform;

namespace Siming.Tests;

public sealed class StartupRegistrationTests
{
    [Fact]
    public void BuildCommand_QuotesPathAndStartsHidden()
    {
        Assert.Equal(
            "\"C:\\Program Files\\Siming\\Siming.exe\" --tray",
            StartupRegistration.BuildCommand(@"C:\Program Files\Siming\Siming.exe"));
    }

    [Theory]
    [InlineData("\"D:\\tools\\Siming\\Siming.exe\" --tray")]
    [InlineData("  \"D:\\tools\\Siming\\Siming.exe\" --tray  ")]
    [InlineData("\"d:\\TOOLS\\siming\\SIMING.EXE\" --TRAY")]
    public void IsSameCommand_AcceptsSamePathIgnoringCaseAndPadding(string registryValue)
    {
        Assert.True(StartupRegistration.IsSameCommand(registryValue, @"D:\tools\Siming\Siming.exe"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"D:\\old\\Siming.exe\" --tray")]
    [InlineData("\"D:\\tools\\Siming\\Siming.exe\"")]
    public void IsSameCommand_RejectsDifferentCommand(string? registryValue)
    {
        Assert.False(StartupRegistration.IsSameCommand(registryValue, @"D:\tools\Siming\Siming.exe"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(new byte[0], false)]
    [InlineData(new byte[] { 0x02, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x06, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x03, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x07, 0, 0, 0 }, true)]
    public void IsApprovedDisabled_ReadsWindowsFlag(byte[]? value, bool expected)
    {
        Assert.Equal(expected, StartupRegistration.IsApprovedDisabled(value));
    }

    [Fact]
    public void Decide_LeavesRegistryAlone_WhenConfigFailedToLoad()
    {
        Assert.Equal(
            StartupAction.None,
            StartupRegistration.Decide(configLoaded: false, enabled: true, new StartupStatus(StartupState.Disabled, null, null)));

        Assert.Equal(
            StartupAction.None,
            StartupRegistration.Decide(configLoaded: false, enabled: false, new StartupStatus(StartupState.Enabled, "x", null)));
    }

    [Theory]
    [InlineData(StartupState.Disabled)]
    [InlineData(StartupState.OtherPath)]
    public void Decide_RegistersWhenConfigWantsIt(StartupState state)
    {
        Assert.Equal(
            StartupAction.Register,
            StartupRegistration.Decide(configLoaded: true, enabled: true, new StartupStatus(state, null, null)));
    }

    [Fact]
    public void Decide_DoesNotFightTaskManager_WhenDisabledBySystem()
    {
        Assert.Equal(
            StartupAction.None,
            StartupRegistration.Decide(
                configLoaded: true,
                enabled: true,
                new StartupStatus(StartupState.DisabledBySystem, "x", null)));
    }

    [Fact]
    public void Decide_DoesNothing_WhenAlreadyEnabled()
    {
        Assert.Equal(
            StartupAction.None,
            StartupRegistration.Decide(configLoaded: true, enabled: true, new StartupStatus(StartupState.Enabled, "x", null)));
    }

    [Theory]
    [InlineData(StartupState.Enabled)]
    [InlineData(StartupState.DisabledBySystem)]
    [InlineData(StartupState.OtherPath)]
    public void Decide_RemovesStaleEntry_WhenConfigIsOff(StartupState state)
    {
        Assert.Equal(
            StartupAction.Unregister,
            StartupRegistration.Decide(configLoaded: true, enabled: false, new StartupStatus(state, "x", null)));
    }

    [Theory]
    [InlineData(StartupState.Disabled)]
    [InlineData(StartupState.Error)]
    public void Decide_DoesNothing_WhenConfigIsOffAndEntryIsGone(StartupState state)
    {
        Assert.Equal(
            StartupAction.None,
            StartupRegistration.Decide(configLoaded: true, enabled: false, new StartupStatus(state, null, null)));
    }
}
