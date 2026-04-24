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
    // Запускаем гарантированно ПОСЛЕ SpriterDotNetBehaviour (default execution order = 0), чтобы
    // TryComputeAggBounds читал уже применённые в текущем кадре bone-трансформы. Иначе Unity по
    // документации не гарантирует порядок LateUpdate между скриптами с одинаковым execution order —
    // median мог бы считаться по stale-позициям предыдущего кадра.
    [DefaultExecutionOrder(100)]
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
        private readonly List<float> sampledWorldMax = new List<float>(BOUNDS_SAMPLE_FRAMES);
        private readonly List<float> sampledCenterX = new List<float>(BOUNDS_SAMPLE_FRAMES);
        private readonly List<float> sampledCenterY = new List<float>(BOUNDS_SAMPLE_FRAMES);
        private readonly List<float> sampledMaxY = new List<float>(BOUNDS_SAMPLE_FRAMES);
        private int framesWaited;
        // Точное значение с сервера. Если задано — скалим сразу без измерения bounds.
        private float? serverSize;
        // Fallback на случай если bounds не появились (старый/сломанный scml). scml-units, native.
        private float? xmlCanvasFallback;
        // Кешируем collider один раз — Phase 2 читает offset каждый кадр до Destroy, лишние GetComponent'ы не нужны.
        private CapsuleCollider2D cachedCapsule;
        // Для принудительного воспроизведения idle-клипа в Phase 1: walk/attack-позы раздувают bbox
        // и портят median. Если idle-клип разрешён (через server entity_actions или scml animation[0]) —
        // форсим его во время sampling'а, чтобы норма считалась по «телу в покое».
        private SpriterDotNetBehaviour cachedBehaviour;
        private string idleClipName;

        public void Init(Transform lifeBar, Transform spritesRoot, float? serverSize, float? xmlCanvasFallback, SpriterDotNetBehaviour behaviour, string idleClipName)
        {
            this.lifeBar = lifeBar;
            this.spritesRoot = spritesRoot;
            this.serverSize = serverSize;
            this.xmlCanvasFallback = xmlCanvasFallback;
            this.cachedCapsule = GetComponent<CapsuleCollider2D>();
            this.cachedBehaviour = behaviour;
            this.idleClipName = idleClipName;
        }

        // State machine:
        //   PhaseSampleSize (scaleApplied=false): накапливаем N сэмплов размера, применяем scale.
        //   PhaseSamplePos  (scaleApplied=true, sampledCenterX.Count < N): накапливаем N сэмплов центра и верха
        //     ПОСЛЕ применённого scale, применяем shift и LifeBar и уничтожаемся.
        void LateUpdate()
        {
            if (spritesRoot == null) { Destroy(this); return; }
            framesWaited++;

            // ФАЗА 1 — определение scale-фактора.
            if (!scaleApplied)
            {
                // Форсим idle-клип каждый Phase-1-кадр: если EntityModel.SetData успел Play'нуть walk/attack,
                // мы его перезаписываем обратно на idle. Иначе median-bounds получит выбросы от широких поз.
                // Condition: Animator уже инициализирован (Start SpriterDotNetBehaviour отработал), клип есть
                // в scml, и сейчас играется НЕ он (чтобы не рестартовать анимацию каждый кадр зря).
                if (cachedBehaviour != null && cachedBehaviour.Animator != null
                    && !string.IsNullOrEmpty(idleClipName)
                    && cachedBehaviour.Animator.HasAnimation(idleClipName)
                    && (cachedBehaviour.Animator.CurrentAnimation == null
                        || cachedBehaviour.Animator.CurrentAnimation.Name != idleClipName))
                {
                    cachedBehaviour.Animator.Play(idleClipName);
                }

                float parentLossy = spritesRoot.parent != null ? spritesRoot.parent.lossyScale.y : 1f;
                if (parentLossy < 0.0001f) parentLossy = 1f;

                // 1.a) Server size — применяется мгновенно, без замеров.
                if (serverSize.HasValue && serverSize.Value > 0.0001f)
                {
                    float t = TARGET_HEIGHT / (parentLossy * serverSize.Value);
                    Vector3 s = spritesRoot.localScale;
                    spritesRoot.localScale = new Vector3(s.x * t, s.y * t, s.z);
                    scaleApplied = true;
                    framesWaited = 0; // старт Phase 2 sampling'а
                    return;
                }

                // 1.b) Median-замер world-bounds. sampledWorldMax = max(x, y) — чтобы и вытянутые существа
                //      (mech'и, корабли) умещались в 1 клетку. У гуманоидов Y > X — поведение не меняется.
                if (TryComputeAggBounds(out Bounds agg) && agg.size.y > 0.0001f)
                    sampledWorldMax.Add(Mathf.Max(agg.size.x, agg.size.y));

                if (sampledWorldMax.Count < BOUNDS_SAMPLE_FRAMES && framesWaited < BOUNDS_SAMPLE_FRAMES * 2)
                    return;

                if (sampledWorldMax.Count > 0)
                {
                    sampledWorldMax.Sort();
                    float medianMax = sampledWorldMax[sampledWorldMax.Count / 2];
                    float t = TARGET_HEIGHT / medianMax;
                    Vector3 s = spritesRoot.localScale;
                    spritesRoot.localScale = new Vector3(s.x * t, s.y * t, s.z);
                }
                else if (xmlCanvasFallback.HasValue && xmlCanvasFallback.Value > 0.0001f)
                {
                    // Fallback: за весь таймаут ни одного валидного сэмпла (Spriter сломан) — XML canvas.
                    float t = TARGET_HEIGHT / (parentLossy * xmlCanvasFallback.Value);
                    Vector3 s = spritesRoot.localScale;
                    spritesRoot.localScale = new Vector3(s.x * t, s.y * t, s.z);
                }
                scaleApplied = true;
                framesWaited = 0; // старт Phase 2 sampling'а
                return;
            }

            // ФАЗА 2 — после scale: накапливаем медиану центра bbox'а и его верха (в root-local)
            // для устойчивого one-shot shift'а сущности. Анимация каждый кадр двигает center на ±0.1-0.2u,
            // один замер ловил случайный кадр — отсюда residual. Медиана по 8 кадрам гасит дрейф.
            if (TryComputeAggBounds(out Bounds agg2) && agg2.size.y > 0.0001f)
            {
                Vector3 centerLocal = transform.InverseTransformPoint(agg2.center);
                Vector3 topLocal = transform.InverseTransformPoint(new Vector3(agg2.center.x, agg2.max.y, 0));
                sampledCenterX.Add(centerLocal.x);
                sampledCenterY.Add(centerLocal.y);
                sampledMaxY.Add(topLocal.y);
            }
            if (sampledCenterX.Count < BOUNDS_SAMPLE_FRAMES && framesWaited < BOUNDS_SAMPLE_FRAMES * 2)
                return;

            // Выравниваем центр силуэта на центр collider'а (где mouse-picking область).
            // Если collider отсутствует — fallback на transform.position (root-local (0,0)).
            Vector2 targetLocal = cachedCapsule != null ? cachedCapsule.offset : Vector2.zero;

            if (sampledCenterX.Count > 0)
            {
                sampledCenterX.Sort(); sampledCenterY.Sort(); sampledMaxY.Sort();
                float medCX = sampledCenterX[sampledCenterX.Count / 2];
                float medCY = sampledCenterY[sampledCenterY.Count / 2];
                float medTopY = sampledMaxY[sampledMaxY.Count / 2];

                Vector3 sp = spritesRoot.localPosition;
                float shiftX = targetLocal.x - medCX;
                float shiftY = targetLocal.y - medCY;
                spritesRoot.localPosition = new Vector3(sp.x + shiftX, sp.y + shiftY, sp.z);

                if (lifeBar != null)
                {
                    // Верх после сдвига = medTopY + shiftY в root-local. LifeBar ставим чуть выше — margin 0.25u
                    // задан в world-units (одинаковый визуальный зазор у всех сущностей вне зависимости от
                    // root.lossyScale.y, который после UpdateController fallback-normalize обычно ≠ 1).
                    float worldMargin = 0.25f;
                    float rootLossyY = Mathf.Max(Mathf.Abs(transform.lossyScale.y), 0.0001f);
                    float localMargin = worldMargin / rootLossyY;
                    Vector3 pos = lifeBar.localPosition;
                    pos.y = medTopY + shiftY + localMargin;
                    lifeBar.localPosition = pos;
                }
            }

            // Баг-фикс state-divergence после force-idle в Phase 1:
            // Пока мы форсили idle, EntityModel.SetData мог получить с сервера action="walk/attack"
            // и закешировать его в EntityModel.action, но Play ушёл нам под руки (мы перезаписали на idle).
            // Следующий server-пакет с тем же action: `changed = action != recive.action` = false,
            // `nonLoop` = false (idle обычно loop) → SetData пропускает Play. Сущность зависает в idle.
            // Лечим явно: если у EntityModel закеширован action ≠ то, что сейчас играется — Play нужный клип.
            // Делается ровно один раз, здесь (до Destroy(this)), чтобы не править EntityModel.SetData.
            var em = GetComponent<EntityModel>();
            if (em != null && cachedBehaviour != null && cachedBehaviour.Animator != null
                && !string.IsNullOrEmpty(em.action) && !string.IsNullOrEmpty(em.prefab))
            {
                string targetClip = AnimationCacheService.GetClipNameSimple(em.prefab, em.action, ConnectController.entity_actions) ?? em.action;
                if (!string.IsNullOrEmpty(targetClip)
                    && cachedBehaviour.Animator.HasAnimation(targetClip)
                    && (cachedBehaviour.Animator.CurrentAnimation == null
                        || cachedBehaviour.Animator.CurrentAnimation.Name != targetClip))
                {
                    cachedBehaviour.Animator.Play(targetClip);
                }
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
        /// <param name="prefabName">Имя Prefab (из library, соответствует EntityRecive.prefab) — используется
        /// для резолва idle-клипа через server-маппинг entity_actions (action ConnectController.idle_action).
        /// Если null или нет маппинга — adjuster сэмплит то, что сейчас играется (без фолбэка на animation[0],
        /// т.к. первая scml-анимация не гарантированно idle).</param>
        /// <param name="bodyHeight">Высота «тела» в scml-единицах (per-prefab, с сервера через Prefab.size
        /// в library — AnimationCacheService.GetPrefabSize(prefabName)). Если null — adjuster fallback'ает
        /// на автозамер bounds.y за N кадров (менее точно).</param>
        public static SpriterDotNetBehaviour CreateSpriter(SpriterPacket packet, string entityName, int gameId, string prefabName = null, float? bodyHeight = null)
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
            // Старый adjuster от предыдущего CreateSpriter мог ещё жить (пере-спавн сущности) — его spritesRoot
            // сейчас уничтожен, в следующий LateUpdate он сам себя Destroy'ит по null-guard'у, но в промежутке
            // мог бы неожиданно стрельнуть на новом visual'е. Сносим явно.
            var existingAdjuster = go.GetComponent<SpriterPostImportAdjuster>();
            if (existingAdjuster != null) GameObject.DestroyImmediate(existingAdjuster);

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

            // Резолв idle-клипа для форсирования в Phase 1 sampling через server entity_actions:
            // action ConnectController.idle_action → имя scml-клипа для этой entity. Имя action-а для idle
            // серверное (по умолчанию "idle"), чтобы не хардкодить литерал в клиенте — см. SigninRecive.idle_action.
            // Если маппинг не найден (не настроено в админке или entity_actions ещё не пришли) — adjuster
            // сэмплит то, что сейчас играется (animation[0] по умолчанию в SpriterDotNet). Fallback'а на
            // entity.Animations[0].Name намеренно нет — первая анимация scml не гарантированно idle, а наугад
            // форсить не-idle позу хуже чем оставить дефолт.
            string idleClipName = null;
            if (!string.IsNullOrEmpty(prefabName))
                idleClipName = AnimationCacheService.GetClipNameSimple(prefabName, ConnectController.idle_action, ConnectController.entity_actions);

            var lifeBar = go.transform.Find("LifeBar");
            var adjuster = go.AddComponent<SpriterPostImportAdjuster>();
            adjuster.Init(lifeBar, sprites.transform, bodyHeight, xmlCanvasFallback, behaviour, idleClipName);

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
        /// Парсит scml XML и возвращает max scml-размер сущности в world-units (не только высоту).
        /// Используется для нормализации — клиент скалит так, чтобы max(W, H) = 1 клетка карты,
        /// и широкие существа (mech'и, корабли) тоже умещались в клетку.
        /// Приоритет источников:
        ///   1) Canvas bounds: max(<animation r-l>, <animation b-t>) из всех <animation l t r b>.
        ///      Атрибуты опциональны — не все scml-генераторы их пишут.
        ///   2) Fallback: max(<file width>, <file height>) среди всех <folder>/<file> — самый
        ///      крупный sprite (обычно body или крыло, близкий к body extent).
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
                var invariant = System.Globalization.CultureInfo.InvariantCulture;

                // 1) Canvas bounds: берём max(width, height) по всем animation'ам.
                float minL = float.PositiveInfinity, maxR = float.NegativeInfinity;
                float minT = float.PositiveInfinity, maxB = float.NegativeInfinity;
                bool anyCanvasW = false, anyCanvasH = false;
                foreach (System.Xml.XmlNode node in doc.SelectNodes("//entity/animation"))
                {
                    if (!(node is System.Xml.XmlElement e)) continue;
                    if (e.HasAttribute("l") && e.HasAttribute("r") &&
                        float.TryParse(e.GetAttribute("l"), System.Globalization.NumberStyles.Float, invariant, out float l) &&
                        float.TryParse(e.GetAttribute("r"), System.Globalization.NumberStyles.Float, invariant, out float r))
                    {
                        if (l < minL) minL = l;
                        if (r > maxR) maxR = r;
                        anyCanvasW = true;
                    }
                    if (e.HasAttribute("t") && e.HasAttribute("b") &&
                        float.TryParse(e.GetAttribute("t"), System.Globalization.NumberStyles.Float, invariant, out float t) &&
                        float.TryParse(e.GetAttribute("b"), System.Globalization.NumberStyles.Float, invariant, out float b))
                    {
                        if (t < minT) minT = t;
                        if (b > maxB) maxB = b;
                        anyCanvasH = true;
                    }
                }
                float canvasW = anyCanvasW && maxR > minL ? maxR - minL : 0f;
                float canvasH = anyCanvasH && maxB > minT ? maxB - minT : 0f;
                float canvasMax = Mathf.Max(canvasW, canvasH);
                if (canvasMax > 0f) return canvasMax / 100f;

                // 2) Fallback: max(<file width>, <file height>) — самый крупный sprite.
                float maxFileDim = 0;
                foreach (System.Xml.XmlNode node in doc.SelectNodes("//folder/file"))
                {
                    if (!(node is System.Xml.XmlElement e)) continue;
                    if (e.HasAttribute("width") &&
                        float.TryParse(e.GetAttribute("width"), System.Globalization.NumberStyles.Float, invariant, out float w) &&
                        w > maxFileDim) maxFileDim = w;
                    if (e.HasAttribute("height") &&
                        float.TryParse(e.GetAttribute("height"), System.Globalization.NumberStyles.Float, invariant, out float h) &&
                        h > maxFileDim) maxFileDim = h;
                }
                return maxFileDim > 0 ? (float?)(maxFileDim / 100f) : null;
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
