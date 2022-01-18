using System;
using UnityEditor;

public class AutoIncrementVersion
{
#if UNITY_CLOUD_BUILD
    public static void PreExport(UnityEngine.CloudBuild.BuildManifestObject manifest)
    {
        PlayerSettings.bundleVersion = String.Format(PlayerSettings.bundleVersion + ".{0}", manifest.GetValue("buildNumber", "unknown"));
    }
#endif
}
