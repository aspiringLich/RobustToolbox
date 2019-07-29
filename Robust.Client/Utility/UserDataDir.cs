using System;
using System.IO;
using JetBrains.Annotations;

namespace Robust.Client.Utility
{
    public static class UserDataDir
    {
        [Pure]
        public static string GetUserDataDir()
        {
            string appDataDir;

#if LINUX
            var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (xdgDataHome == null)
            {
                appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            }
            else
            {
                appDataDir = xdgDataHome;
            }
#elif MACOS
            appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                      "Library", "Application Support");
#else
            appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
#endif

            return Path.Combine(appDataDir, "Space Station 14");
        }

    }
}
