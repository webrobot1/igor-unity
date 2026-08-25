using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Mmogick.VideoRig
{
    /// <summary>
    /// Оснастка съёмки роликов: прогоняет сценарий действий персонажа и пишет на каждую его сцену свой
    /// видеофрагмент. Живёт в редакторной сборке — в игру к игроку не попадает.
    ///
    /// Порядок прогона: войти в игру обычным путём (Play Mode, кнопка «Войти»), затем позвать
    /// <see cref="Run"/>. Ждать загрузки мира не нужно — прогон ждёт его сам.
    ///
    /// Каталог вывода — параметр. Пакет записи пишет только по локальному пути Windows: сетевое имя чужой
    /// файловой системы он молча проглатывает, не создав файла и ничего не сказав, — поэтому фрагменты
    /// пишутся в локальный каталог, а к сборщику ролика их переносит тот, кто прогон запустил.
    /// </summary>
    public static class VideoRig
    {
        /// <summary>Куда пишутся фрагменты, когда запускающий не назвал каталог.</summary>
        private const string DEFAULT_OUTPUT = "C:/Temp/mmogick-video";

        /// <summary>Ход последнего прогона и снятые им фрагменты — их опрашивает запускающий.</summary>
        public static string Status
        {
            get { return ScenarioRunner.Status; }
        }

        public static List<string> Files
        {
            get { return ScenarioRunner.Files; }
        }

        /// <summary>
        /// Запустить сценарий. <paramref name="outputDir"/> пуст — каталог собирается из
        /// <see cref="DEFAULT_OUTPUT"/> и имени сценария.
        /// </summary>
        public static string Run(string scenarioPath, string outputDir = null)
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException("сценарий снимается только в запущенной игре");

            ShootScenario scenario = ShootScenario.Load(scenarioPath);

            if (string.IsNullOrEmpty(outputDir))
                outputDir = Path.Combine(DEFAULT_OUTPUT, scenario.name ?? Path.GetFileNameWithoutExtension(scenarioPath));

            outputDir = outputDir.Replace('\\', '/');

            // Размер окна игры задаёт и размер снимаемого кадра, и экранные координаты сценария. Пакет
            // выставляет его сам в начале записи — ставим заранее, чтобы интерфейс уже стоял на своих
            // местах, когда сценарий начнёт целиться в его кнопки.
            PlayModeWindow.SetCustomRenderingResolution((uint)scenario.width, (uint)scenario.height, "VideoRig");

            // Носитель переживает смену сцены: прогон запускают сразу после нажатия «Войти», а игровая
            // сцена грузится уже после — обычный объект она снесла бы вместе с прогоном.
            GameObject host = new GameObject("VideoRig") { hideFlags = HideFlags.DontSave };
            UnityEngine.Object.DontDestroyOnLoad(host);

            ScenarioHost behaviour = host.AddComponent<ScenarioHost>();
            behaviour.StartCoroutine(new ScenarioRunner(scenario, outputDir, host).Play());

            return "сценарий " + scenario.name + ": сцен " + scenario.scenes.Count + ", вывод " + outputDir;
        }

        /// <summary>
        /// Снять перехват ввода на загрузке редактора и на выходе из игры. Перехват — статическое
        /// состояние, а домен между запусками игры в этом проекте не перезагружается: оставшийся от
        /// прерванного прогона перехват сделал бы игру неуправляемой мышью, и причина этого была бы не
        /// видна ниоткуда.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ReleaseInput()
        {
            InputSource.EndScript();

            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode)
                    InputSource.EndScript();
            };
        }
    }
}
