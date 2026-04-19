using SpriterDotNet;
using SpriterDotNetUnity;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Mmogick
{
    /// <summary>
    /// Пост-импорт нормализация Spriter-сущности: применяет равномерный scale на "Sprites"-child,
    /// чтобы все Spriter-сущности имели одинаковую целевую высоту (TARGET_HEIGHT = 1 клетка).
    /// Источник bodyHeight (по убыванию точности):
    ///   1) serverSize — с сервера через /prefabs library (точное значение, задан админом).
    ///   2) Измерение actual world-bounds рендеренных Spriter-детей (после 1 кадра, когда UnityAnimator
    ///      применит transforms) + compensation по parentLossy — native scml-extent.
    ///   3) xmlCanvasFallback — canvas-bounds из scml <animation l t r b> или max file.height,
    ///      если bounds не готовы (сухой fallback на случай если измерение не удалось).
    /// После scale позиционирует LifeBar над фактическим верхом и самоуничтожается.
    /// </summary>
    internal class SpriterPostImportAdjuster : MonoBehaviour
    {
        /// <summary>
        /// Целевая высота Spriter-сущности в мировых юнитах = число клеток карты по Y.
        /// Grid, создаваемый в MapController, имеет cellSize=(1,1,0) по умолчанию — т.е. 1 клетка = 1 юнит.
        /// </summary>
        public const float TARGET_HEIGHT = 1.0f;

        /// <summary>
        /// Количество кадров-сэмплов bounds для median-замера. Медиана устойчива к выбросам
        /// (кадр атаки с оружием над головой, crouch) — в отличие от одного замера. 8 кадров ≈ 0.13 сек
        /// при 60 fps, незаметно для игрока. Если за это время bounds так и не появились — fallback.
        /// </summary>
        private const int BOUNDS_SAMPLE_FRAMES = 8;

        private Transform lifeBar;
        private Transform spritesRoot;
        private bool scaleApplied;
        private readonly List<float> sampledWorldHeights = new List<float>(BOUNDS_SAMPLE_FRAMES);
        private int framesWaited;
        // Точное значение с сервера. Если задано — скалим сразу без измерения bounds.
        private float? serverSize;
        // Fallback на случай если bounds не появились (старый/сломанный scml). scml-units, native.
        private float? xmlCanvasFallback;

        public void Init(Transform lifeBar, Transform spritesRoot, float? serverSize, float? xmlCanvasFallback)
        {
            this.lifeBar = lifeBar;
            this.spritesRoot = spritesRoot;
            this.serverSize = serverSize;
            this.xmlCanvasFallback = xmlCanvasFallback;
        }

        void LateUpdate()
        {
            if (spritesRoot == null) { Destroy(this); return; }

            // Фаза 1 — нормализация размера.
            if (!scaleApplied)
            {
                float parentLossy = spritesRoot.parent != null ? spritesRoot.parent.lossyScale.y : 1f;
                if (parentLossy < 0.0001f) parentLossy = 1f;

                // 1) Server size (точное значение) — применяем сразу, без замера bounds.
                //    bodyHeight в scml world-units. world_h = parentLossy × localScale × bodyHeight = TARGET_HEIGHT
                //    → localScale = TARGET_HEIGHT / (parentLossy × bodyHeight).
                if (serverSize.HasValue && serverSize.Value > 0.0001f)
                {
                    float t = TARGET_HEIGHT / (parentLossy * serverSize.Value);
                    spritesRoot.localScale = new Vector3(t, t, spritesRoot.localScale.z);
                    scaleApplied = true;
                    return;
                }

                // 2) Median-замер world-bounds за N кадров (устойчив к выбросам — кадры атаки с оружием
                //    над головой / crouch'ы игнорируются как крайние значения). bounds в WORLD координатах —
                //    parentLossy уже внутри. Накапливаем sample'ы пока не соберём BOUNDS_SAMPLE_FRAMES.
                //    Семплируем MAX(x, y) — чтобы широкие существа (летающие boss'ы, корабли) тоже
                //    умещались в 1 клетку. У гуманоидов Y > X, поведение не меняется.
                if (TryComputeAggBounds(out Bounds agg) && agg.size.y > 0.0001f)
                {
                    sampledWorldHeights.Add(Mathf.Max(agg.size.x, agg.size.y));
                }
                framesWaited++;

                // Ждём пока не наберём достаточно сэмплов (или пока не исчерпаем таймаут в BOUNDS_SAMPLE_FRAMES*2 кадров).
                if (sampledWorldHeights.Count < BOUNDS_SAMPLE_FRAMES && framesWaited < BOUNDS_SAMPLE_FRAMES * 2)
                    return;

                if (sampledWorldHeights.Count > 0)
                {
                    sampledWorldHeights.Sort();
                    float medianWorldMax = sampledWorldHeights[sampledWorldHeights.Count / 2];
                    float t = TARGET_HEIGHT / medianWorldMax;
                    Vector3 s = spritesRoot.localScale;
                    spritesRoot.localScale = new Vector3(s.x * t, s.y * t, s.z);
                    scaleApplied = true;
                    return;
                }

                // 3) Последний fallback — XML canvas (overestimate, но хоть что-то). scml world-units.
                //    Срабатывает если за весь таймаут не собрали ни одного валидного bounds-сэмпла
                //    (Spriter сломан или sprites ещё не готовы).
                if (xmlCanvasFallback.HasValue && xmlCanvasFallback.Value > 0.0001f)
                {
                    float t = TARGET_HEIGHT / (parentLossy * xmlCanvasFallback.Value);
                    spritesRoot.localScale = new Vector3(t, t, spritesRoot.localScale.z);
                }
                scaleApplied = true;
                return;
            }

            // Фаза 2 — позиционирование LifeBar над актуальным верхом (после scale).
            if (lifeBar != null && TryComputeAggBounds(out Bounds aggLB))
            {
                Vector3 topLocal = transform.InverseTransformPoint(new Vector3(transform.position.x, aggLB.max.y, 0f));
                Vector3 pos = lifeBar.localPosition;
                pos.y = topLocal.y + 0.25f;
                lifeBar.localPosition = pos;
            }
            Destroy(this);
        }

        // Агрегированный world-AABB всех активных SR-детей spritesRoot по tight-rect (непрозрачные пиксели).
        // Возвращает false, если ещё ничего не анимировалось/не отрендерилось (bounds.y ~ 0).
        private bool TryComputeAggBounds(out Bounds agg)
        {
            agg = default;
            bool hasAny = false;
            foreach (var sr in spritesRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null || sr.sprite == null || !sr.enabled) continue;
                Bounds b;
                if (AnimationCacheService.TryGetTightRect(sr.sprite, out Rect tight))
                {
                    Vector3 w00 = sr.transform.TransformPoint(new Vector3(tight.xMin, tight.yMin, 0));
                    Vector3 w10 = sr.transform.TransformPoint(new Vector3(tight.xMax, tight.yMin, 0));
                    Vector3 w01 = sr.transform.TransformPoint(new Vector3(tight.xMin, tight.yMax, 0));
                    Vector3 w11 = sr.transform.TransformPoint(new Vector3(tight.xMax, tight.yMax, 0));
                    b = new Bounds(w00, Vector3.zero);
                    b.Encapsulate(w10); b.Encapsulate(w01); b.Encapsulate(w11);
                }
                else b = sr.bounds;
                if (!hasAny) { agg = b; hasAny = true; }
                else agg.Encapsulate(b);
            }
            return hasAny && agg.size.y > 0.0001f;
        }
    }

    public class NewSpriterRuntimeImporter
    {
        private struct SpriterEntityData
        {
            public SpriterEntity entity;
            public SpriterData data;

            public SpriterEntityData(SpriterEntity entity, SpriterData data)
            {
                this.entity = entity;
                this.data = data;
            }
        }

        private static readonly Dictionary<string, SpriterEntityData> SpriterEntityDatas = new Dictionary<string, SpriterEntityData>();

        private static readonly string ObjectNameSprites = "Sprites";
        private static readonly string ObjectNameMetadata = "Metadata";
        /// <param name="bodyHeight">Высота «тела» в scml-единицах (per-prefab, с сервера через EntityRecive.body_height).
        /// Если null — adjuster fallback'ает на автозамер bounds.y за N кадров (менее точно).</param>
        public static SpriterDotNetBehaviour CreateSpriter(SpriterPacket packet, string entityName, int gameId, float? bodyHeight = null)
        {
            GameObject go = GameObject.Find(entityName);
            if (go == null)
                throw new Exception("При создании анимации объект более не сущетвует на сцене");

            // Всегда сносим предыдущий визуал — это может быть либо старая Spriter-инициализация,
            // либо fallback-wrapper "Sprites" который UpdateController создаёт при спавне из корневого SpriteRenderer.
            var existingBehaviour = go.GetComponent<SpriterDotNetBehaviour>();
            if (existingBehaviour != null) GameObject.DestroyImmediate(existingBehaviour);
            var existingSprites = go.transform.Find(ObjectNameSprites);
            if (existingSprites != null) GameObject.DestroyImmediate(existingSprites.gameObject);
            var existingMetadata = go.transform.Find(ObjectNameMetadata);
            if (existingMetadata != null) GameObject.DestroyImmediate(existingMetadata.gameObject);

            SpriterDotNetBehaviour behaviour = go.AddComponent<SpriterDotNetBehaviour>();
            SpriterEntity entity = FetchOrCacheSpriterEntityDataFromFile(packet, entityName, behaviour, gameId);

            GameObject sprites = new GameObject(ObjectNameSprites);
            GameObject metadata = new GameObject(ObjectNameMetadata);

            behaviour.UseNativeTags = false;
            if (SpriterImporterUtil.HasSound(entity)) go.AddComponent<AudioSource>();

            sprites.SetParent(go);
            metadata.SetParent(go);

            ChildData cd = new ChildData();
            SpriterImporterUtil.CreateSprites(entity, cd, sprites);
            SpriterImporterUtil.CreateCollisionRectangles(entity, cd, metadata);
            SpriterImporterUtil.CreatePoints(entity, cd, metadata);
            cd.Verify();

            behaviour.EntityIndex = entity.Id;
            behaviour.enabled = true;
            behaviour.ChildData = cd;

            // Spriter успешно установлен. Legacy-Animator на корне сносим (у player.prefab он был
            // фолбэком; при приходе scml-анимации он уже не нужен). Корневой SpriteRenderer НЕ сносим,
            // а только выключаем: TargetController.CameraUpdate читает его spriteRender.sprite как
            // репрезентативный fallback-спрайт для настройки face-камеры — если его убрать,
            // target-панель начинает ловить только один body-part Spriter-mirror'а и показывает огрызок.
            // Рендерить fallback-спрайт не нужно — это делают Spriter-дети — достаточно enabled=false.
            Animator fallbackAnimator = go.GetComponent<Animator>();
            if (fallbackAnimator != null) GameObject.DestroyImmediate(fallbackAnimator);

            SpriteRenderer fallbackSpriteRenderer = go.GetComponent<SpriteRenderer>();
            if (fallbackSpriteRenderer != null) fallbackSpriteRenderer.enabled = false;
            // Кешированная ObjectModel.animator — Unity-null после DestroyImmediate выше, "!= null" guard'ы отработают.

            // Adjuster сам выбирает источник bodyHeight в порядке точности:
            //   1) serverSize (параметр bodyHeight) — из /prefabs library, задан админом.
            //   2) Актуальные world-bounds рендеренных Spriter-детей (один sample через 1 кадр после
            //      инициализации) — native body extent в WORLD координатах, самый точный автозамер.
            //   3) xmlCanvasFallback — canvas-bounds или max file.height из packet.xml (overestimate
            //      ~1.5-2x body, fallback на случай если (2) не сработает).
            float? xmlCanvasFallback = ParseScmlCanvasHeight(packet.xml);
            var lifeBar = go.transform.Find("LifeBar");
            var adjuster = go.AddComponent<SpriterPostImportAdjuster>();
            adjuster.Init(lifeBar, sprites.transform, bodyHeight, xmlCanvasFallback);

            return behaviour;
        }

        /// <summary>
        /// Создаёт автономную Spriter-анимацию на <paramref name="targetGo"/>, переиспользуя SpriterData/Entity с уже собранного источника.
        /// Нужно для живого отображения Spriter-цели в target-UI (где раньше показывалась legacy Animator-анимация).
        /// Все дочерние "Sprites"/"Metadata" и любой SpriterDotNetBehaviour на targetGo будут пересозданы.
        /// </summary>
        public static SpriterDotNetBehaviour MirrorFromSource(SpriterDotNetBehaviour source, GameObject targetGo)
        {
            ClearMirror(targetGo);
            if (source == null || source.SpriterData == null || source.SpriterData.Spriter == null) return null;

            var entities = source.SpriterData.Spriter.Entities;
            if (entities == null || entities.Length == 0) return null;
            SpriterEntity entity = entities[0];

            SpriterDotNetBehaviour behaviour = targetGo.AddComponent<SpriterDotNetBehaviour>();
            behaviour.SpriterData = source.SpriterData;
            behaviour.UseNativeTags = source.UseNativeTags;

            GameObject sprites = new GameObject(ObjectNameSprites);
            GameObject metadata = new GameObject(ObjectNameMetadata);
            sprites.SetParent(targetGo);
            metadata.SetParent(targetGo);

            ChildData cd = new ChildData();
            SpriterImporterUtil.CreateSprites(entity, cd, sprites);
            SpriterImporterUtil.CreateCollisionRectangles(entity, cd, metadata);
            SpriterImporterUtil.CreatePoints(entity, cd, metadata);
            cd.Verify();

            // Положим Spriter-детям тот же layer, что и у target-UI GameObject, чтобы face_camera их видела.
            SetLayerRecursively(sprites, targetGo.layer);
            SetLayerRecursively(metadata, targetGo.layer);

            behaviour.EntityIndex = entity.Id;
            behaviour.enabled = true;
            behaviour.ChildData = cd;
            return behaviour;
        }

        /// <summary>
        /// Снимает ранее собранный Spriter-mirror с <paramref name="targetGo"/> (удаляет SpriterDotNetBehaviour и дочерние Sprites/Metadata).
        /// </summary>
        public static void ClearMirror(GameObject targetGo)
        {
            if (targetGo == null) return;
            var existing = targetGo.GetComponent<SpriterDotNetBehaviour>();
            if (existing != null) GameObject.DestroyImmediate(existing);
            var oldSprites = targetGo.transform.Find(ObjectNameSprites);
            if (oldSprites != null) GameObject.DestroyImmediate(oldSprites.gameObject);
            var oldMetadata = targetGo.transform.Find(ObjectNameMetadata);
            if (oldMetadata != null) GameObject.DestroyImmediate(oldMetadata.gameObject);
        }

        private static void SetLayerRecursively(GameObject root, int layer)
        {
            if (root == null) return;
            root.layer = layer;
            foreach (Transform t in root.transform)
                SetLayerRecursively(t.gameObject, layer);
        }

        /// <summary>
        /// Парсит scml XML и возвращает ориентировочную высоту тела в scml world-units.
        /// Приоритет источников:
        ///   1) Y-extent canvas'а: max(<animation b>) - min(<animation t>) из всех <animation l t r b>.
        ///      SCML Y растёт вниз: t отрицательный (верх), b положительный (низ). Атрибуты опциональны —
        ///      не все scml-генераторы их пишут (видим пустой t/b в БД для анимаций от некоторых тулов).
        ///   2) Fallback: max <file height> среди всех <folder>/<file> — высота самого высокого sprite'a.
        ///      Грубо приближает «высоту тела» (тело обычно чуть меньше, но близко, т.к. tallest sprite
        ///      часто — это корпус или крыло, которые и формируют body bounds).
        /// SpriterDotNet библиотека оба источника игнорирует. Парсим сами XmlDocument-ом.
        /// Null если в scml нет ни того, ни другого.
        /// </summary>
        private static float? ParseScmlCanvasHeight(string xml)
        {
            if (string.IsNullOrEmpty(xml)) return null;
            try
            {
                var doc = new System.Xml.XmlDocument();
                doc.LoadXml(xml);

                // 1) Canvas bounds из <animation l t r b>.
                float minT = float.PositiveInfinity, maxB = float.NegativeInfinity;
                bool anyCanvas = false;
                foreach (System.Xml.XmlNode node in doc.SelectNodes("//entity/animation"))
                {
                    if (!(node is System.Xml.XmlElement e)) continue;
                    if (!e.HasAttribute("t") || !e.HasAttribute("b")) continue;
                    if (float.TryParse(e.GetAttribute("t"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float t) &&
                        float.TryParse(e.GetAttribute("b"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float b))
                    {
                        if (t < minT) minT = t;
                        if (b > maxB) maxB = b;
                        anyCanvas = true;
                    }
                }
                if (anyCanvas && maxB > minT) return (maxB - minT) / 100f;

                // 2) Fallback: max <file height> (tallest sprite). Это верхняя оценка body.
                float maxFileH = 0;
                foreach (System.Xml.XmlNode node in doc.SelectNodes("//folder/file"))
                {
                    if (!(node is System.Xml.XmlElement e) || !e.HasAttribute("height")) continue;
                    if (float.TryParse(e.GetAttribute("height"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float h))
                        if (h > maxFileH) maxFileH = h;
                }
                return maxFileH > 0 ? (float?)(maxFileH / 100f) : null;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("ParseScmlCanvasHeight: fallback к null — " + ex.Message);
                return null;
            }
        }

        private static SpriterEntity FetchOrCacheSpriterEntityDataFromFile(SpriterPacket packet, string entityName, SpriterDotNetBehaviour spriterDotNetBehaviour, int gameId)
        {
            if (SpriterEntityDatas.TryGetValue(entityName, out SpriterEntityData cachedEntityData))
            {
                spriterDotNetBehaviour.SpriterData = cachedEntityData.data;
                return cachedEntityData.entity;
            }

            Spriter spriter = SpriterReader.Default.Read(packet.xml);

            if(spriter.Entities.Length>1)
                throw new Exception("В наборе может быть одна сущность с анимациями");

            SpriterEntity entity = spriter.Entities[0];

            SpriterData spriterData = ScriptableObject.CreateInstance<SpriterData>();
            spriterData.Spriter = spriter;
            spriterData.FileEntries = LoadAssets(spriter, packet.files, gameId).ToArray();

            SpriterEntityData entityData = new SpriterEntityData(entity, spriterData);
            SpriterEntityDatas[entity.Name] = entityData;
           
            spriterDotNetBehaviour.SpriterData = spriterData;

            return entity;
        }

        private static IEnumerable<SdnFileEntry> LoadAssets(Spriter spriter, Dictionary<int, string> files, int gameId)
        {
            foreach (SpriterFolder folder in spriter.Folders)
            {
                foreach (SpriterFile file in folder.Files)
                {
                    files.TryGetValue(file.Id, out string fileName);
                    yield return new SdnFileEntry
                    {
                        FolderId = folder.Id,
                        FileId   = file.Id,
                        Sprite   = AnimationCacheService.GetSprite(gameId, fileName),
                    };
                }
            }
        }
    }
}
