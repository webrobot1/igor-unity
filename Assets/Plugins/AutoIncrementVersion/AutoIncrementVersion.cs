using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// https://bigcheeseapp.com/2019/04/25/automatically-set-ios-build-number-and-android-version-code-in-unity-cloud-build/
public class AutoIncrementVersion
{
#if UNITY_CLOUD_BUILD
    public static void PreExport(UnityEngine.CloudBuild.BuildManifestObject manifest)
    {
        string buildNum = "unknown";

        Debug.LogFormat($"PreExport:: PlayerSettings.WebGL.template = '{PlayerSettings.WebGL.template}'");

        manifest.TryGetValue<string>("buildNumber", out buildNum);
        Debug.LogFormat($"AutoIncrementVersion.PreExport() called, build number is {buildNum}");

        string versionString = $"{Application.version}.{buildNum}";
        Debug.LogFormat($"PlayerSettings.bundleVersion set to {versionString}");

        PlayerSettings.bundleVersion = versionString;
    }
#endif
}
