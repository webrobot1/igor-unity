#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using System.Linq;

using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;

[InitializeOnLoad]
public class Startup : ScriptableObject
{
    static Startup()
    {
        // проверим необходимые пакеты
        /*        var pack = Client.List();
                while (!pack.IsCompleted);

                // todo передалить в list
                var pckName = "com.unity.cinemachine";

                if (pack.Result.FirstOrDefault(q => q.name == pckName) == null)
                {
                   var add =  Client.Add(pckName);
                   while (!pack.IsCompleted);

                   Debug.Log(pckName + " успешно установлен");
                }*/

        // если первая загрузка c Git и нет сцен
        if (EditorBuildSettings.scenes.Length != 2)
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

        // Play Mode в редакторе стартует с первой сцены списка сборки — сцены входа, как и собранная игра, —
        // какие бы сцены ни были открыты: при разработке можно постоянно держать открытой MainScene.
        // Стартовая сцена задаётся до входа в Play Mode: объекты открытых сцен получают Awake при самом входе,
        // а объекты игровой сцены читают в нём то, что кладёт только вход в игру.
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.ExitingEditMode)
                return;

            string path = EditorBuildSettings.scenes[0].path;
            SceneAsset start = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);

            if (start == null)
                throw new InvalidOperationException("Первой сцены списка сборки " + path + " нет в проекте — Play Mode не с чего стартовать");

            // Изменённые сцены предлагается сохранить до старта. Открытая стартовая сцена уходит в игру такой, какая
            // она в редакторе: несохранённая правка в ней видна в игре и после отказа сохранять (Don't Save).
            // Отказ (Cancel) отменяет и сам старт игры.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorApplication.isPlaying = false;
                return;
            }

            EditorSceneManager.playModeStartScene = start;
        };


        GraphicsSettings.transparencySortMode = TransparencySortMode.CustomAxis;
        GraphicsSettings.transparencySortAxis = new Vector3(0, 1f, -1f);

        // везде используем Net 4.
        // PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Android, ApiCompatibilityLevel.NET_4_6);
        // PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.WebGL, ApiCompatibilityLevel.NET_4_6);

        // EditorSettings.unityRemoteDevice =;
    }
}
#endif