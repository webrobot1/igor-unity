using SpriterDotNet;
using SpriterDotNetUnity;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

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

        private Transform lifeBar;
        private Transform spritesRoot;
        private int framesRemaining = 2;
        private bool scaleApplied;

        public void Init(Transform lifeBar, Transform spritesRoot)
        {
            this.lifeBar = lifeBar;
            this.spritesRoot = spritesRoot;
        }

        void LateUpdate()
        {
            if (spritesRoot == null) { Destroy(this); return; }
            if (framesRemaining-- > 0) return;

            Bounds agg = default;
            bool hasAny = false;
            foreach (var sr in spritesRoot.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (sr == null || sr.sprite == null || !sr.enabled) continue;
                if (!hasAny) { agg = sr.bounds; hasAny = true; }
                else agg.Encapsulate(sr.bounds);
            }

            if (!hasAny || agg.size.y < 0.0001f)
            {
                Destroy(this);
                return;
            }

            // 1) нормализация размера — чтобы у всех сущностей визуальная высота была одинаковой.
            // Масштабируем именно "Sprites"-контейнер, а не entity-root — иначе поехал бы Collider2D и LifeBar anchor'ы.
            if (!scaleApplied)
            {
                float factor = TARGET_HEIGHT / agg.size.y;
                Vector3 s = spritesRoot.localScale;
                spritesRoot.localScale = new Vector3(s.x * factor, s.y * factor, s.z);
                scaleApplied = true;

                // Bounds теперь устарели — подождём ещё один кадр, чтобы LifeBar поставился по новому верху.
                framesRemaining = 1;
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

            // Spriter успешно установлен — убираем резервные визуалы префаба НА КОРНЕ целиком,
            // а не просто выключаем. Раньше они отключались, но оставались на объекте —
            // лишние компоненты, участвующие в инициализации, держащие ссылки на AnimatorController/Sprite.
            // При наличии Spriter'а fallback-визуал больше не нужен в принципе.
            Animator fallbackAnimator = go.GetComponent<Animator>();
            SpriteRenderer fallbackSpriteRenderer = go.GetComponent<SpriteRenderer>();

            // SortingGroup: все дочерние SpriteRenderer'ы Spriter-анимации будут сортироваться как
            // единое целое относительно других сущностей. Это устраняет взаимное просачивание
            // частей тела между двумя персонажами, возникающее при Transparency Sort Mode = Custom Axis
            // (когда сортировка идёт по проекции на custom-axis каждого спрайта по отдельности).
            var sortingGroup = go.GetComponent<SortingGroup>();
            if (sortingGroup == null) sortingGroup = go.AddComponent<SortingGroup>();
            // перенесём sortingOrder с корневого SpriteRenderer на группу (его выставляет UpdateController по spawn_sort).
            if (fallbackSpriteRenderer != null && sortingGroup.sortingOrder == 0)
                sortingGroup.sortingOrder = fallbackSpriteRenderer.sortingOrder;

            if (fallbackAnimator != null) GameObject.DestroyImmediate(fallbackAnimator);
            if (fallbackSpriteRenderer != null) GameObject.DestroyImmediate(fallbackSpriteRenderer);
            // Кешированные ссылки (ObjectModel.animator и т.п.) трогать не надо —
            // Unity-null семантика DestroyImmediate сделает их fake-null, и существующие "!= null" guard'ы отработают.

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
