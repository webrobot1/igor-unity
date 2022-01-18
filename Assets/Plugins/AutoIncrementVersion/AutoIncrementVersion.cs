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
        // to update major or minor version, manually set it in Edit>Project Settings>Player>Other Settings>Version

        // this will also set Application.version which is readonly
        int[] versionParts = PlayerSettings.bundleVersion.Split('.');
        if (versionParts.Length != 3 || versionParts[2].ParseInt(-1) == -1)
        {
            Debug.LogError("BuildPostprocessor failed to update version " + PlayerSettings.bundleVersion);
            return;
        }
        // major-minor-build
        versionParts[2] = (versionParts[2].ParseInt() + 1).ToString();
        PlayerSettings.bundleVersion = versionParts.Join(".");
    }
#endif
}
