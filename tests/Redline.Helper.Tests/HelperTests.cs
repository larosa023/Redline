using System.Security.AccessControl;
using System.Security.Principal;
using Redline.Helper;
using Redline.Ipc;
using Xunit;

namespace Redline.Helper.Tests;

public class HelperTests
{
    [Fact]
    public void InstallPath_IsUnderProgramFiles_Versioned()
    {
        var p = InstallLayout.HelperDir("1.2.3");
        Assert.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), p);
        Assert.EndsWith(Path.Combine("Redline", "Helper", "1.2.3"), p);
        Assert.DoesNotContain("AppData", p);
    }

    [Fact]
    public void Acl_UsersCannotWrite()
    {
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var rules = InstallLayout.ProtectedFolder().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>();
        var userRights = rules.Where(r => r.IdentityReference.Equals(users) && r.AccessControlType == AccessControlType.Allow)
                              .Aggregate((FileSystemRights)0, (a, r) => a | r.FileSystemRights);
        Assert.Equal((FileSystemRights)0, userRights & (FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.Delete | FileSystemRights.ChangePermissions));
        Assert.True(InstallLayout.ProtectedFolder().AreAccessRulesProtected);
    }

    [Fact]
    public void PipeSecurity_DeniesNetwork()
    {
        var net = new SecurityIdentifier(WellKnownSidType.NetworkSid, null);
        var rules = InstallLayout.Pipe().GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<System.IO.Pipes.PipeAccessRule>();
        Assert.Contains(rules, r => r.IdentityReference.Equals(net) && r.AccessControlType == AccessControlType.Deny);
    }

    [Fact]
    public async Task Service_Runs_Engine_StartStop_AnswersOverPipe()
    {
        var pipe = "redline-helper-test-" + Guid.NewGuid().ToString("N");
        var svc = new HelperService(Path.Combine(Path.GetTempPath(), "rl-" + Guid.NewGuid().ToString("N")), demo: true, pipe);
        await svc.StartAsync(default);
        try
        {
            await using var client = await RetryConnect(pipe);
            var r = await client.RequestAsync(MessageTypes.GetState, null, TimeSpan.FromSeconds(10));
            Assert.Equal(MessageTypes.Ok, r.Type);
            Assert.Equal("Redline Demo GPU", Assert.Single(r.PayloadAs<HelperState>()!.Devices).Name);
        }
        finally { await svc.StopAsync(default); }
    }

    static async Task<PipeClient> RetryConnect(string pipe)
    {
        for (var i = 0; ; i++)
        {
            try { return await PipeClient.ConnectAsync(pipe, TimeSpan.FromSeconds(2)); }
            catch (HelperUnavailableException) when (i < 10) { await Task.Delay(500); }
        }
    }
}
