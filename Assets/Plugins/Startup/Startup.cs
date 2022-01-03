#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using System.Linq;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
public class Startup : ScriptableObject
{
    static Startup()
    {
        // проверим необходимые пакеты
        var pckName = "com.unity.cinemachine";
        var pack = Client.List();
        while (!pack.IsCompleted);

        if (pack.Result.FirstOrDefault(q => q.name == pckName) == null)
        {
           var add =  Client.Add(pckName);
           while (!pack.IsCompleted);

           Debug.Log(pckName + " успешно установлен");
        }

        // если первая загрузка c Git и нет сцен
        if(EditorBuildSettings.scenes.Length == 0)
        {
            EditorBuildSettingsScene[] scenes = new EditorBuildSettingsScene[2];
            scenes[0] = new EditorBuildSettingsScene();
            scenes[0].path = "Assets/Scenes/RegisterScene.unity";
            scenes[0].enabled = true;

            scenes[1] = new EditorBuildSettingsScene();
            scenes[1].path = "Assets/Scenes/MainScene.unity";
            scenes[1].enabled = true;

            EditorBuildSettings.scenes = scenes;
        }

        // везде используем Net 4.
        PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_4_6);
        PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.WebGL, ApiCompatibilityLevel.NET_4_6);

       // EditorSettings.unityRemoteDevice =;
    }
}
#endif