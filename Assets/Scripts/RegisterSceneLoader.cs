#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// При разработке в Unity в Play Mode (режим отладки) автоматически активирует сцену регистрации (так что при разработке можно постоянно держать открыто MainScene). 
/// А при сборке регистрация стоит перовой сценой и неважно какая была открыты в Unity
/// </summary>
[InitializeOnLoadAttribute]
public static class RegisterSceneLoader
{
	static RegisterSceneLoader()
	{
		EditorApplication.playModeStateChanged += LoadDefaultScene;
	}

	static void LoadDefaultScene(PlayModeStateChange state)
	{
		if (state == PlayModeStateChange.ExitingEditMode)
		{
			EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
		}

		if (state == PlayModeStateChange.EnteredPlayMode)
		{
			EditorSceneManager.LoadScene(0);
		}
	}
}
#endif