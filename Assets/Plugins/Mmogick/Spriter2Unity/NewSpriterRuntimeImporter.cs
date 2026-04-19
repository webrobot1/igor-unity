using SpriterDotNet;
using SpriterDotNetUnity;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Mmogick
{
    /// <summary>
    /// Пост-импорт нормализация Spriter-сущности: после нескольких кадров (пока UnityAnimator
    /// заполнит transforms) агрегирует Bounds активных SpriteRenderer'ов и применяет:
    ///   1) равномерный scale на "Sprites"-child — чтобы все Spriter-сущности имели одинаковую
    ///      целевую высоту (targetHeight), независимо от размеров изображений внутри scml.
    ///   2) позиционирование LifeBar над реальным верхом уже нормализованных спрайтов.
    /// Самоуничтожается после одной корректировки.
    /// </summary>
    internal class SpriterPostImportAdjuster : MonoBehaviour
    {
        /// <summary>
        /// Целевая высота Spriter-сущности в мировых юнитах = число клеток карты по Y.
        /// Grid, создаваемый в MapController, имеет cellSize=(1,1,0) по умолчанию — т.е. 1 клетка = 1 юнит.
        /// step (ConnectController.step) сюда не подходит: это размер шага в юнитах, он может быть
        /// меньше/больше клетки (полшага, спринт и т.п.), а размер персонажа должен зависеть от клетки,
        /// а не от темпа его движения.
        /// </summary>
        public const float TARGET_HEIGHT = 1.0f;

        /// <summary>
        /// Сколько кадров сэмплируем bounds перед принятием решения о масштабе.
        /// Нужно тк на стартовом кадре UnityAnimator может дать нерепрезентативную позу
        /// (части тела не успели разложиться), а поздний кадр другой анимации — наоборот с выбросами
        /// типа огня/оружия над головой. Берём медиану у_max по выборке — устойчиво и к выбросам.
        /// </summary>
        private const int SAMPLE_FRAMES = 12;

        private Transform lifeBar;
        private Transform spritesRoot;
        private readonly List<float> yMaxSamples = new List<float>(SAMPLE_FRAMES);
        private readonly List<float> yMinSamples = new List<float>(SAMPLE_FRAMES);
        private int framesSampled;
        private bool scaleApplied;

        public void Init(Transform lifeBar, Transform spritesRoot)
        {
            this.lifeBar = lifeBar;
            this.spritesRoot = spritesRoot;
        }

        void LateUpdate()
        {
            if (spritesRoot == null) { Destroy(this); return; }

            Bounds agg = default;
            bool hasAny = false;
            foreach (var sr in spritesRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null || sr.sprite == null || !sr.enabled) continue;
                // Используем tight-rect (опаковые пиксели) вместо sr.bounds: sr.bounds в Unity считает
                // всю sprite.rect включая прозрачные поля PNG — персонажи с большими пустыми областями
                // нормализовались бы в клетку мельче остальных.
                Bounds b;
                if (AnimationCacheService.TryGetTightRect(sr.sprite, out Rect tight))
                {
                    // 4 угла tight-rect в sprite-local (pivot=(0,0), низ-лево = начало координат).
                    Vector3 w00 = sr.transform.TransformPoint(new Vector3(tight.xMin, tight.yMin, 0));
                    Vector3 w10 = sr.transform.TransformPoint(new Vector3(tight.xMax, tight.yMin, 0));
                    Vector3 w01 = sr.transform.TransformPoint(new Vector3(tight.xMin, tight.yMax, 0));
                    Vector3 w11 = sr.transform.TransformPoint(new Vector3(tight.xMax, tight.yMax, 0));
                    b = new Bounds(w00, Vector3.zero);
                    b.Encapsulate(w10); b.Encapsulate(w01); b.Encapsulate(w11);
                }
                else
                {
                    b = sr.bounds;
                }
                if (!hasAny) { agg = b; hasAny = true; }
                else agg.Encapsulate(b);
            }

            if (!hasAny || agg.size.y < 0.0001f)
            {
                // ещё ничего не анимировалось — подождём следующий кадр.
                return;
            }

            if (!scaleApplied)
            {
                yMaxSamples.Add(agg.max.y);
                yMinSamples.Add(agg.min.y);
                framesSampled++;
                if (framesSampled < SAMPLE_FRAMES) return;

                // Медиана вместо среднего/max — режет выбросы (огненные/магические эффекты над головой,
                // кончик оружия, выкинутый в сторону) и даёт устойчивую «высоту тела».
                yMaxSamples.Sort();
                yMinSamples.Sort();
                float medMax = yMaxSamples[yMaxSamples.Count / 2];
                float medMin = yMinSamples[yMinSamples.Count / 2];
                float bodyHeight = medMax - medMin;
                if (bodyHeight < 0.0001f) { Destroy(this); return; }

                float factor = TARGET_HEIGHT / bodyHeight;
                Vector3 s = spritesRoot.localScale;
                spritesRoot.localScale = new Vector3(s.x * factor, s.y * factor, s.z);
                scaleApplied = true;

                // Подождём ещё один кадр — bounds теперь устарели, LifeBar поставим по новому верху.
                return;
            }

            // 2) размещение LifeBar над уже нормализованными спрайтами.
            if (lifeBar != null)
            {
                Vector3 topLocal = transform.InverseTransformPoint(new Vector3(transform.position.x, agg.max.y, 0f));
                Vector3 pos = lifeBar.localPosition;
                pos.y = topLocal.y + 0.25f;
                lifeBar.localPosition = pos;
            }
            Destroy(this);
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
        public static SpriterDotNetBehaviour CreateSpriter(SpriterPacket packet, string entityName, int gameId)
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

            // Spriter успешно установлен — сносим резервные визуалы на корне. SortingGroup уже добавлен
            // UpdateController'ом при спавне.
            Animator fallbackAnimator = go.GetComponent<Animator>();
            if (fallbackAnimator != null) GameObject.DestroyImmediate(fallbackAnimator);

            SpriteRenderer fallbackSpriteRenderer = go.GetComponent<SpriteRenderer>();
            if (fallbackSpriteRenderer != null) GameObject.DestroyImmediate(fallbackSpriteRenderer);
            // Кешированные ссылки (ObjectModel.animator и т.п.) — Unity-null после DestroyImmediate, "!= null" guard'ы отработают.

            // LifeBar в префабе позиционирован под fallback-sprite. У Spriter-сущности другой bounding box —
            // adjuster нормализует размер (все сущности к единой высоте) и поднимет LifeBar над фактическим верхом.
            var lifeBar = go.transform.Find("LifeBar");
            var adjuster = go.AddComponent<SpriterPostImportAdjuster>();
            adjuster.Init(lifeBar, sprites.transform);

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
