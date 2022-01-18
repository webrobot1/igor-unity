using UnityEngine;
using UnityEditor;
using System;

public class AutoIncrementVersion : MonoBehaviour
{
#if UNITY_CLOUD_BUILD
    public static void PreExport(UnityEngine.CloudBuild.BuildManifestObject manifest)
    {
        manifest.SetValue("buildNumber", String.Format(Application.version + ".{0}", manifest.GetValue("buildNumber", "unknown")));
    }
#endif
}
