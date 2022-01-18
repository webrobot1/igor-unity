using UnityEngine;
using UnityEditor;
using System;

public class AutoIncrementVersion : MonoBehaviour
{
    public static void PreExport(UnityEngine.CloudBuild.BuildManifestObject manifest)
    {
        PlayerSettings.bundleVersion = String.Format(PlayerSettings.bundleVersion + ".{0}", manifest.GetValue("buildNumber", "unknown"));
    }
}
