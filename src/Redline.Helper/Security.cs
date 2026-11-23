using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Redline.Helper;

[SupportedOSPlatform("windows")]
public static class InstallLayout
{
    public const string ServiceName = "RedlineHelper";

    /// <summary>%ProgramFiles%\Redline\Helper\&lt;version&gt; — admin-protected, never the user-writable app folder.</summary>
    public static string HelperDir(string version) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Redline", "Helper", version);

    public static string DataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Redline");

    static SecurityIdentifier Sid(WellKnownSidType t) => new(t, null);

    /// <summary>SYSTEM and Administrators: full control. Users: read and run only. No inheritance from the parent.</summary>
    public static DirectorySecurity ProtectedFolder()
    {
        var s = new DirectorySecurity();
        s.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        const InheritanceFlags inh = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
        s.AddAccessRule(new FileSystemAccessRule(Sid(WellKnownSidType.LocalSystemSid), FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
        s.AddAccessRule(new FileSystemAccessRule(Sid(WellKnownSidType.BuiltinAdministratorsSid), FileSystemRights.FullControl, inh, PropagationFlags.None, AccessControlType.Allow));
        s.AddAccessRule(new FileSystemAccessRule(Sid(WellKnownSidType.BuiltinUsersSid), FileSystemRights.ReadAndExecute, inh, PropagationFlags.None, AccessControlType.Allow));
        return s;
    }

    /// <summary>Data folder: same, Users may read (settings shown in the app come through the pipe anyway).</summary>
    public static DirectorySecurity DataFolder() => ProtectedFolder();

    /// <summary>Pipe: SYSTEM/Admins full; logged-in interactive users read/write; network access denied.</summary>
    public static PipeSecurity Pipe()
    {
        var p = new PipeSecurity();
        p.AddAccessRule(new PipeAccessRule(Sid(WellKnownSidType.NetworkSid), PipeAccessRights.FullControl, AccessControlType.Deny));
        p.AddAccessRule(new PipeAccessRule(Sid(WellKnownSidType.LocalSystemSid), PipeAccessRights.FullControl, AccessControlType.Allow));
        p.AddAccessRule(new PipeAccessRule(Sid(WellKnownSidType.BuiltinAdministratorsSid), PipeAccessRights.FullControl, AccessControlType.Allow));
        p.AddAccessRule(new PipeAccessRule(Sid(WellKnownSidType.InteractiveSid), PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return p;
    }

    public static NamedPipeServerStream CreatePipe(string name) =>
        NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, Pipe());
}
