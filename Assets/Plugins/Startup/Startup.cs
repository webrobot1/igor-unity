#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.PackageManager.Requests;
using UnityEditor.PackageManager;
using UnityEngine;
using System.Linq;
using System.Collections;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public class Startup
{
    //Unity calls the static constructor when the engine opens
    static Startup()
    {
        var pckName = "com.unity.cinemachine";
        var pack = Client.List();
        while (!pack.IsCompleted);

        if (pack.Result.FirstOrDefault(q => q.name == pckName) == null)
        {
           var add =  Client.Add(pckName);
           while (!pack.IsCompleted);

           Debug.Log(pckName + " успешно установлен");
        }

        if (!SceneManager.GetSceneByName("RegisterScene").IsValid())
        {
            // если по ошибке стоит текущей
            SceneManager.UnloadSceneAsync("MainScene");

            // в тч при первлй установке из GIT
            SceneManager.LoadScene("RegisterScene");

            Debug.Log("Сцена заменнена на RegisterScene");
        }
    }
}

#endif