using System;
using System.IO;

namespace DBLC;

/// <summary>Resolves DBLC's writable per-user data root, with an optional isolated build profile.</summary>
public static class AppDataPaths
{
    public static bool IsIsolatedRuntime
    {
        get
        {
#if DBLC_EXPERIMENTAL_ISOLATION
            return true;
#else
            return !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DBLC_DATA_ROOT"));
#endif
        }
    }

    public static string Root
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("DBLC_DATA_ROOT");
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
#if DBLC_EXPERIMENTAL_ISOLATION
            return Path.Combine(local, "DBLC-GMStreamEffects-Experimental");
#else
            return Path.Combine(local, "DBLC");
#endif
        }
    }

    public static string File(params string[] parts) => Path.Combine([Root, .. parts]);
}
