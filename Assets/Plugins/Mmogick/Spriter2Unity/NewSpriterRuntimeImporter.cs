using SpriterDotNet;
using SpriterDotNetUnity;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Mmogick
{
    /// <summary>
    /// Подтягивает LifeBar над реальным верхом Spriter-персонажа.
    /// Ждёт пару кадров после запуска SpriterDotNetBehaviour (transforms заполняются UnityAnimator'ом),
    /// затем измеряет агрегированные Bounds активных SpriteRenderer'ов и сдвигает LifeBar.
    /// Самоуничтожается после одной корректировки.
    /// </summary>
    internal class SpriterLifeBarAdjuster : MonoBehaviour
    {
        private Transform lifeBar;
        private Transform spritesRoot;
        private int framesRemaining = 2;

        public void Init(Transform lifeBar, Transform spritesRoot)
        {
            this.lifeBar = lifeBar;
            this.spritesRoot = spritesRoot;
        }

        void LateUpdate()
        {
            if (lifeBar == null || spritesRoot == null) { Destroy(this); return; }
            if (framesRemaining-- > 0) return;

            Bounds agg = default;
            bool hasAny = false;
            foreach (var sr in spritesRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null || sr.sprite == null || !sr.enabled) continue;
                if (!hasAny) { agg = sr.bounds; hasAny = true; }
                else agg.Encapsulate(sr.bounds);
            }

            // Двигаем LifeBar только если bounds валидные (не нулевой размер).
            if (hasAny && agg.size.sqrMagnitude > 0.0001f)
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

            if (go.GetComponent<SpriterDotNetBehaviour>() != null)
            {
                GameObject.DestroyImmediate(go.GetComponent<SpriterDotNetBehaviour>());
                if(go.transform.Find(ObjectNameSprites)!=null)
                    GameObject.DestroyImmediate(go.transform.Find(ObjectNameSprites));

                if (go.transform.Find(ObjectNameMetadata) != null)
                    GameObject.DestroyImmediate(go.transform.Find(ObjectNameMetadata));
            }

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

            // Spriter успешно установлен — глушим резервные визуалы префаба на корне,
            // чтобы legacy-Animator (overrideController) и корневой SpriteRenderer
            // не проигрывали фолбэк-анимацию (например, Dead) параллельно со Spriter'ом.
            Animator fallbackAnimator = go.GetComponent<Animator>();
            if (fallbackAnimator != null) fallbackAnimator.enabled = false;

            SpriteRenderer fallbackSpriteRenderer = go.GetComponent<SpriteRenderer>();
            if (fallbackSpriteRenderer != null) fallbackSpriteRenderer.enabled = false;

            // LifeBar в префабе позиционирован под fallback-sprite (скелет).
            // У Spriter-персонажа фактическая высота другая — поднимем LifeBar над реальным bounding box.
            var lifeBar = go.transform.Find("LifeBar");
            if (lifeBar != null)
            {
                var adjuster = go.AddComponent<SpriterLifeBarAdjuster>();
                adjuster.Init(lifeBar, sprites.transform);
            }

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
