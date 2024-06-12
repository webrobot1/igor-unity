using SpriterDotNet;
using SpriterDotNetUnity;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Mmogick
{
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
        public static SpriterDotNetBehaviour CreateSpriter(SpriterPacket packet, string entityName, Transform parent = null)
        {
            GameObject go = GameObject.Find(entityName);
            if (go == null)
                throw new Exception("При создании анимации объект более не сущетвует на сцене");

            SpriterDotNetBehaviour behaviour = go.AddComponent<SpriterDotNetBehaviour>();
            SpriterEntity entity = FetchOrCacheSpriterEntityDataFromFile(packet, entityName, behaviour);

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

            go.transform.parent = parent;
            return behaviour;
        }

        private static SpriterEntity FetchOrCacheSpriterEntityDataFromFile(SpriterPacket packet, string entityName, SpriterDotNetBehaviour spriterDotNetBehaviour)
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
            spriterData.FileEntries = LoadAssets(packet.files).ToArray();

            SpriterEntityData entityData = new SpriterEntityData(entity, spriterData);
            SpriterEntityDatas[entity.Name] = entityData;
           
            spriterDotNetBehaviour.SpriterData = spriterData;

            return entity;
        }

        private static IEnumerable<SdnFileEntry> LoadAssets(SpriterFile[] files)
        {
            foreach (SpriterFile file in files)
            {
                SdnFileEntry entry = new SdnFileEntry
                {
                    FolderId = file.folder_id,
                    FileId = file.id,
                };
                entry.Sprite = file.Sprite;

                yield return entry;
            }
        }   
    }
}
