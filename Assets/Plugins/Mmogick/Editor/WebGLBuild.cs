using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Mmogick.ClientBuild
{
    /// <summary>
    /// Пакетная сборка клиента под WebGL (Development) для проверок «как в игре» без редактора:
    /// <c>Unity.exe -batchmode -quit -projectPath &lt;проект&gt; -executeMethod Mmogick.ClientBuild.WebGLBuild.Build
    /// -buildOutput &lt;каталог&gt; -logFile &lt;журнал&gt;</c>. Сцены — включённые из списка сборки. Активная платформа
    /// редактора после сборки возвращается прежней. Неуспех — код выхода 1, причина в журнале.
    ///
    /// Рядом со сборкой ложится отметка свежести <see cref="STAMP_FILE"/>: коммит клиента и отпечаток входов —
    /// git-деревья каталогов <see cref="INPUTS"/> в их состоянии на диске, с незакоммиченными и неотслеживаемыми
    /// (не игнорируемыми) файлами. Снимается до сборки: правка по ходу сборки делает копию несвежей, а не
    /// проходит незамеченной. Расчёт — git во временном индексе, индекс пользователя не трогается; повторяется
    /// вне Unity теми же командами из корня репозитория клиента:
    /// <c>GIT_INDEX_FILE=&lt;новый файл&gt; git read-tree HEAD</c>, <c>git add -A -- Assets ProjectSettings Packages</c>,
    /// <c>git write-tree</c>, затем <c>git rev-parse &lt;дерево&gt;:&lt;каталог&gt;</c> по каждому каталогу.
    /// Сборка свежая, пока все три дерева равны записанным; коммит в отметке — для человека, свежести не решает.
    /// Игнорируемые git файлы в отпечаток не входят.
    /// </summary>
    public static class WebGLBuild
    {
        private const string STAMP_FILE = "build-stamp.json";

        /// <summary>Метка формата отметки: меняется вместе с составом её полей либо расчётом отпечатка.</summary>
        private const int STAMP_FORMAT = 1;

        private static readonly string[] INPUTS = { "Assets", "ProjectSettings", "Packages" };

        public static void Build()
        {
            BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup previousGroup = BuildPipeline.GetBuildTargetGroup(previousTarget);
            int exitCode = 1;

            try
            {
                string output = Argument("-buildOutput");
                string stampPath = Path.Combine(output, STAMP_FILE);

                // Прежняя отметка рядом с недособранной копией выдавала бы её за свежую сборку прежних входов.
                if (File.Exists(stampPath))
                    File.Delete(stampPath);
                string stamp = Stamp();

                string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
                if (scenes.Length == 0)
                    throw new InvalidOperationException("В списке сборки нет включённых сцен");

                // Сборка под неактивную платформу сама её не переключает: скрипты игрока компилируются без сборок
                // платформы (UnityEngine.WebGLModule) и падают ошибками компиляции.
                if (previousTarget != BuildTarget.WebGL && !EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL))
                    throw new InvalidOperationException("Не удалось переключить платформу редактора на WebGL");

                BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = output,
                    target = BuildTarget.WebGL,
                    targetGroup = BuildTargetGroup.WebGL,
                    options = BuildOptions.Development,
                });

                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Сборка WebGL не удалась: " + report.summary.result + ", ошибок " + report.summary.totalErrors);

                File.WriteAllText(stampPath, stamp);
                Debug.Log("Сборка WebGL готова: " + output + ", " + report.summary.totalTime);
                exitCode = 0;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                if (EditorUserBuildSettings.activeBuildTarget != previousTarget)
                    EditorUserBuildSettings.SwitchActiveBuildTarget(previousGroup, previousTarget);
            }

            EditorApplication.Exit(exitCode);
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length)
                throw new ArgumentException("Не передан параметр " + name + " <каталог сборки>");

            return args[index + 1];
        }

        private static string Stamp()
        {
            string index = Path.Combine(Path.GetTempPath(), "mmogick-build-stamp-" + Guid.NewGuid().ToString("N") + ".index");
            try
            {
                string commit = Git("rev-parse HEAD", null).Trim();
                Git("read-tree HEAD", index);
                Git("add -A -- " + string.Join(" ", INPUTS), index);
                string tree = Git("write-tree", index).Trim();
                string[] trees = Git("rev-parse " + string.Join(" ", INPUTS.Select(input => tree + ":" + input)), null)
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

                string inputs = string.Join(",\n", INPUTS.Select((input, i) => "    \"" + input + "\": \"" + trees[i].Trim() + "\""));
                return "{\n  \"format\": " + STAMP_FORMAT + ",\n  \"commit\": \"" + commit + "\",\n  \"inputs\": {\n" + inputs + "\n  }\n}\n";
            }
            finally
            {
                File.Delete(index);
                File.Delete(index + ".lock");
            }
        }

        private static string Git(string arguments, string indexFile)
        {
            var info = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = Directory.GetParent(Application.dataPath).FullName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (indexFile != null)
                info.EnvironmentVariables["GIT_INDEX_FILE"] = indexFile;

            using (Process process = Process.Start(info))
            {
                var error = process.StandardError.ReadToEndAsync();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException("git " + arguments + ": код " + process.ExitCode + ", " + error.Result);

                return output;
            }
        }
    }
}
