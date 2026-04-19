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
    /// Источник bodyHeight (в scml world-units): либо serverSize из /prefabs library (точный, задан админом),
    /// либо canvas-bounds scml <animation l t r b> — парсится в NewSpriterRuntimeImporter.CreateSpriter
    /// из packet.xml. Если оба null — нормализация не применяется, Spriter рендерится в native scml scale.
    /// После scale позиционирует LifeBar над фактическим верхом и самоуничтожается.
    /// </summary>
    internal class SpriterPostImportAdjuster : MonoBehaviour
    {
        /// <summary>
        /// Целевая высота Spriter-сущности в мировых юнитах = число клеток карты по Y.
        /// Grid, создаваемый в MapController, имеет cellSize=(1,1,0) по умолчанию — т.е. 1 клетка = 1 юнит.
        /// </summary>
        public const float TARGET_HEIGHT = 1.0f;

        private Transform lifeBar;
        private Transform spritesRoot;
        private bool scaleApplied;
        // scml-единицы: factor = TARGET_HEIGHT / bodyHeight. Null = данных нет (ни серверного size,
        // ни canvas-bounds в scml) → нормализация не применяется.
        private float? bodyHeight;

        public void Init(Transform lifeBar, Transform spritesRoot, float? bodyHeight)
        {
            this.lifeBar = lifeBar;
            this.spritesRoot = spritesRoot;
            this.bodyHeight = bodyHeight;
        }

        void LateUpdate()
        {
            if (spritesRoot == null) { Destroy(this); return; }

            // Фаза 1 — нормализация размера (сразу при первом LateUpdate, без ожидания кадров).
            if (!scaleApplied)
            {
                if (bodyHeight.HasValue && bodyHeight.Value > 0.0001f)
                {
                    float factor = TARGET_HEIGHT / bodyHeight.Value;
                    Vector3 s = spritesRoot.localScale;
                    spritesRoot.localScale = new Vector3(s.x * factor, s.y * factor, s.z);
                }
                scaleApplied = true;
                // Следующий кадр — LifeBar-позиционирование по bounds (уже с учётом scale).
                return;
            }

            // Фаза 2 — позиционирование LifeBar над актуальным верхом (после scale).
            if (lifeBar != null && TryComputeAggBounds(out Bounds agg))
            {
                Vector3 topLocal = transform.InverseTransformPoint(new Vector3(transform.position.x, agg.max.y, 0f));
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

            // Выбор источника bodyHeight для adjuster'а:
            //   1) bodyHeight (параметр) — серверный size из /prefabs library, задан админом. Точнее всего.
            //   2) ParseScmlCanvasHeight — Y-extent всех <animation l t r b> из packet.xml, делённый на PPU=100.
            //      SpriterDotNet эти атрибуты не парсит (они только для Preview в Spriter-редакторе), поэтому
            //      читаем прямо из scml XML. Canvas-bounds включают оружие/эффекты — немного больше body,
            //      но мгновенно и без ожидания кадров.
            //   3) null — у scml нет l/t/r/b (старый/сломанный формат) → adjuster не масштабирует, Spriter
            //      рендерится в native scml scale.
            float? finalBodyHeight = bodyHeight ?? ParseScmlCanvasHeight(packet.xml);
            var lifeBar = go.transform.Find("LifeBar");
            var adjuster = go.AddComponent<SpriterPostImportAdjuster>();
            adjuster.Init(lifeBar, sprites.transform, finalBodyHeight);

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
