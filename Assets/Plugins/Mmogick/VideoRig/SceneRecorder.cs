using System;
using System.IO;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

namespace Mmogick.VideoRig
{
    /// <summary>
    /// Запись кадров игры по границам сцен: на каждую сцену свой файл «идентификатор.mp4». Настройки
    /// собираются один раз, между сценами меняется только имя файла — пересборка на сцену стоила бы
    /// лишнего кадра паузы там, где фрагмент как раз начинается.
    ///
    /// Каталог вывода — параметр; какую форму пути пакет принимает, а какую теряет молча, — у
    /// <see cref="VideoRig"/>.
    /// </summary>
    internal sealed class SceneRecorder
    {
        private readonly string dir;

        private readonly RecorderControllerSettings settings;

        private readonly MovieRecorderSettings movie;

        private readonly RecorderController controller;

        internal SceneRecorder(string dir, int width, int height, float fps)
        {
            this.dir = dir;
            Directory.CreateDirectory(dir);

            movie = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            movie.name = "VideoRig";
            movie.Enabled = true;
            movie.CaptureAudio = false;
            movie.EncoderSettings = new CoreEncoderSettings
            {
                Codec = CoreEncoderSettings.OutputCodec.MP4,
                EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High
            };

            // Снимается окно игры целиком — то же, что видит игрок: интерфейс, указатель, мир. Размер
            // окна пакет выставляет по этим числам сам.
            movie.ImageInputSettings = new GameViewInputSettings { OutputWidth = width, OutputHeight = height };

            settings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            settings.AddRecorderSettings(movie);

            // Ручной режим: границы фрагмента задаёт сценарий, а не интервал времени.
            settings.SetRecordModeToManual();
            settings.FrameRate = fps;

            // Постоянный шаг времени: кадр записывается на каждый шаг игры, и запись идёт ровно с
            // объявленной частотой независимо от того, успевает ли машина считать кадр за 1/fps.
            settings.FrameRatePlayback = FrameRatePlayback.Constant;
            settings.CapFrameRate = true;

            controller = new RecorderController(settings);
        }

        /// <summary>Начать фрагмент сцены. Возвращает путь файла, который получится.</summary>
        internal string Begin(string sceneId)
        {
            movie.OutputFile = Path.Combine(dir, sceneId).Replace('\\', '/');

            controller.PrepareRecording();

            if (!controller.StartRecording())
                throw new InvalidOperationException("запись сцены " + sceneId + " не началась, причина — в консоли редактора");

            return movie.OutputFile + ".mp4";
        }

        internal void End()
        {
            if (controller.IsRecording())
                controller.StopRecording();
        }
    }
}
