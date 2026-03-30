using System.Security.AccessControl;
using System.Security.Principal;

namespace SingBoot;

/// <summary>
/// Ensures only one instance of the application runs at a time, even across different
/// elevation levels, using a named mutex with a permissive security descriptor.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private Mutex? _mutex;

    /// <summary>
    /// Attempts to acquire the single-instance lock.
    /// Returns true if this is the only running instance.
    /// </summary>
    public bool Acquire(string mutexName)
    {
        try
        {
            // Create a security descriptor that allows access from System, Admins, and Authenticated Users
            // so that elevated and non-elevated instances can see the same mutex.
            var security = new MutexSecurity();
            security.AddAccessRule(new MutexAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                MutexRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new MutexAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                MutexRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new MutexAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                MutexRights.FullControl,
                AccessControlType.Allow));

            _mutex = new Mutex(initiallyOwned: true, @"Global\" + mutexName, out bool createdNew, security);

            if (createdNew)
                return true;

            // Another instance already holds the mutex
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch { }
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
