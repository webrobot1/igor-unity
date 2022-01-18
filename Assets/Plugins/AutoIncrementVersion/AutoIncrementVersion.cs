using UnityEngine;
using UnityEditor;
using System;

public class AutoIncrementVersion : MonoBehaviour
{
#if UNITY_CLOUD_BUILD
    public static void PreExport(UnityEngine.CloudBuild.BuildManifestObject manifest)
    {
        PlayerSettings.bundleVersion = String.Format(PlayerSettings.bundleVersion + ".{0}", manifest.GetValue("buildNumber", "unknown"));
    }
#endif
}
