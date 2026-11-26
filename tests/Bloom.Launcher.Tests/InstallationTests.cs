using Bloom.Launcher.Installation;

namespace Bloom.Launcher.Tests;

public sealed class InstallationTests
{
    [Theory]
    [InlineData("\"C:\\Roblox\\RobloxPlayerBeta.exe\" %1", "C:\\Roblox\\RobloxPlayerBeta.exe")]
    [InlineData("C:\\Roblox\\RobloxPlayerBeta.exe %1", "C:\\Roblox\\RobloxPlayerBeta.exe")]
    [InlineData("", null)]
    public void Extracts_executable(string command, string? exe) => Assert.Equal(exe, RobloxInstallation.ExecutableFromCommand(command));

    [Theory]
    [InlineData("\"C:\\R\\RobloxPlayerBeta.exe\" %1", "roblox://placeId=1", "roblox://placeId=1")]
    [InlineData("\"C:\\R\\RobloxPlayerBeta.exe\" \"%1\"", "roblox://placeId=1", "\"roblox://placeId=1\"")]
    [InlineData("\"C:\\R\\RobloxPlayerBeta.exe\"", "roblox://placeId=1", "\"roblox://placeId=1\"")]
    [InlineData("\"C:\\R\\RobloxPlayerBeta.exe\" --app %1", "x", "--app x")]
    public void Builds_arguments(string command, string argument, string expected) =>
        Assert.Equal(expected, RobloxInstallation.ArgumentsFromCommand(command, argument));
}
