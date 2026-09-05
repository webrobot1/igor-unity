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
    /// Сценарий и каталог вывода — параметры; штатно оба лежат в каталоге ролика на сервере, куда Windows
    /// ходит буквой диска, назначенной файловой системе WSL. Только буквой: сетевое имя `\\сервер\...`
    /// сборщик пути пакета записи схлопывает до пути без диска (двойной разделитель в начале сжимает в
    /// один), файл уходит в корень текущего диска, и ошибки при этом не приходит.
    /// </summary>
    public static class VideoRig
    {
        /// <summary>
        /// Запасной каталог фрагментов, когда запускающий не назвал каталог: `Temp/` проекта. Он под игнором
        /// git, а редактор очищает его при своём закрытии — снятое туда забирать до выхода из редактора.
        /// </summary>
        private static string DefaultOutput
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.dataPath), "Temp", "mmogick-video"); }
        }

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
        /// Запустить сценарий. Штатный <paramref name="outputDir"/> — каталог кадров ролика на сервере
        /// (`shots/` в каталоге ролика) через букву диска файловой системы WSL: фрагменты ложатся к
        /// сборщику ролика сразу. Пуст — каталог собирается из <see cref="DefaultOutput"/> и имени файла
        /// сценария.
        /// </summary>
        public static string Run(string scenarioPath, string outputDir = null)
        {
            if (!Application.isPlaying)
                throw new InvalidOperationException("сценарий снимается только в запущенной игре");

            ShootScenario scenario = ShootScenario.Load(scenarioPath);

            // Сценарий зовётся именем своего файла: собственного имени документ не несёт.
            string name = Path.GetFileNameWithoutExtension(scenarioPath);

            if (string.IsNullOrEmpty(outputDir))
                outputDir = Path.Combine(DefaultOutput, name);

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

            return "сценарий " + name + ": сцен " + scenario.scenes.Count + ", вывод " + outputDir;
        }

        /// <summary>
        /// Снять перехват ввода на загрузке редактора и на выходе из игры. Перехват — статическое
        /// состояние, а домен между запусками игры в этом проекте не перезагружается: оставшийся от
        /// прерванного прогона перехват сделал бы игру неуправляемой мышью, и причина этого была бы не
        /// видна ниоткуда.
        ///
        /// Тем же выходом снимается и материал прогона, оборванного на полпути: прогон живёт корутиной
        /// внутри игры и об её остановке не узнаёт вовсе.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ReleaseRun()
        {
            InputSource.EndScript();

            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.ExitingPlayMode)
                    return;

                InputSource.EndScript();
                ScenarioRunner.Abort();
            };
        }
    }
}
