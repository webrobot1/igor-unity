using System.Collections.Generic;
using Spine;
using Spine.Unity;
using Spine.Unity.AttachmentTools;
using UnityEngine;

namespace Mmogick
{
    // Вешает надетые предметы (оружие/щит) на тело сущности. Носитель — ЛЮБАЯ сущность с экипировкой:
    // свой игрок, чужой игрок, моб (компонент equip публичный, приходит всем видящим). Точка входа —
    // Sync: слот + prefab надетого предмета.
    //
    // Отдельного объекта у предмета нет вовсе — кусок предмета (RegionAttachment) ставится в пустой
    // слот-держатель скелета, привязанный сервером к кости якоря (имя держателя приходит в самом якоре).
    // Тем самым предмет живёт внутри меша тела: следует за костью, зеркалится и сортируется вместе с ним.
    // Активен тот якорь, чья кость несёт ПОКАЗАННУЮ в этом кадре кожу — прямой аналог правила дерева
    // («точка присутствует в кадре»): ракурс скелет переключает тем, что кожа одного ракурса надета в свои
    // слоты, а прочие пусты. Кость якоря своих слотов не несёт — она РОДИТЕЛЬ костей, на которых висит кожа
    // ракурса, потому и якорь, и соседи по глубине ищутся по её ПОДДЕРЕВУ. Глубина — место держателя в
    // порядке отрисовки: сервер ставит держатели В КОНЕЦ перечня слотов (в середину нельзя, смещения порядка
    // в клипах адресуют слот его местом), а рисоваться предмет должен поверх кожи СВОЕЙ руки и под тем, что
    // нарисовано дальше, — потому держатель переставляется за последний слот своего поддерева КАЖДЫЙ кадр,
    // после того как клип разложил свой порядок отрисовки (хук UpdateComplete).
    public class WeaponMount : MonoBehaviour
    {
        private const float Ppu = 100f;          // как в AnimationCacheService.GetSprite

        // Доля роста НОСИТЕЛЯ, которую занимает надетый предмет по своей длинной стороне при scale слота = 1.
        // Тело любой сущности нормируется в одну клетку карты (SpineVisualBuilder.TARGET_HEIGHT;
        // клетка Grid = 1 мировой юнит), поэтому «доля клетки» и «доля
        // роста носителя» — одно число. Размер надетого предмета — величина, задаваемая ЗДЕСЬ, а не разрешением
        // исходника: без нормализации предмет получал бы размер своего PNG в масштабе скелета (400px-меч ≈ рост
        // персонажа, 574px-посох — полтора роста). Тонкая подгонка под конкретный слот/скелет — множитель scale
        // слота (сервер).
        private const float MountSpan = 0.6f;

        // Порог пересчёта вершин куска на скелете: RegionAttachment.UpdateSequence аллоцирует массивы вершин
        // и UV, а посадка предмета пересчитывается каждый кадр. Ниже порога изменение неразличимо на глаз, и
        // в покое (постоянные угол и масштаб кости) пересчёта не происходит вовсе.
        private const float PieceRotEpsilon = 0.25f;    // градусы
        private const float PieceScaleEpsilon = 0.002f; // доля самого масштаба

        // Источник одного варианта картинки для Apply: исходный спрайт из кеша (центр-pivot) + хват + направление.
        public struct VariantSource
        {
            public int angle;        // направление, под которое нарисован вариант (0=вправо, 90=вверх, …)
            public Sprite sprite;
            public float pivotX, pivotY;
        }

        // Вариант после Apply: направление → готовый grip-спрайт (pivot хвата).
        private class Variant
        {
            public int angle;
            public Sprite grip;      // создаётся в Apply, освобождается в Release
            public float span;       // длинная сторона видимых пикселей исходника, мировые юниты при scale=1

            // Кусок для слота-держателя и его материал. Материал свой, потому что текстура предмета атласу
            // скелета не принадлежит; шейдер — тот же, каким SpineCacheService собирает страницы атласа
            // тела, иначе предмет и тело рисовались бы по-разному.
            public Material material;
            public RegionAttachment piece;

            // Последняя ПРИМЕНЁННАЯ к куску посадка — вершины пересчитываем только на её смену (см. эпсилоны).
            public bool applied;
            public float appliedScale, appliedX, appliedY, appliedRot;
        }

        private class Mounted
        {
            public string slot;          // slug слота экипировки: по нему резолвятся якоря на скелете носителя
            public Variant[] variants;   // ≥1, в серверном порядке (angle ASC); активный выбирается по ракурсу тела
            public string rotationMode;  // AnimationCacheService.RotationMode.* (mirror_x даёт зеркальных кандидатов)

            public SpineCacheService.SlotAnchor[] anchors;
            public Slot[] holders;       // держатели якорей, по индексам anchors; null — держателя нет
            public int fallback = -1;    // индекс якоря с первым существующим держателем; -1 — ставить некуда
            public bool tried;
            public SkeletonAnimation skeleton;    // скелет, под который якоря резолвили: сменился — резолвим заново
            public Slot holder;                   // держатель, в котором сейчас стоит кусок
        }

        private readonly Dictionary<string, Mounted> _slots = new Dictionary<string, Mounted>();
        private SkeletonAnimation _skel;
        private EntityModel _em;   // DisplayAngle (ракурс играющего клипа) + Forward (fallback) — выбор варианта картинки

        // Надеть/снять предмет в слоте экипировки ЛЮБОЙ сущности: wearer — носитель, itemPrefab — prefab
        // надетого предмета (пусто = снять). Единая точка для своего игрока, чужих игроков и мобов:
        // компонент equip публичный и несёт prefab, инвентарь носителя тут не нужен.
        // Предмет-картинка (image-prefab) — единственный поддерживаемый визуал предмета.
        public static void Sync(EntityModel wearer, string slot, string itemPrefab)
        {
            if (wearer == null)
                return;

            WeaponMount mount = wearer.GetComponent<WeaponMount>();

            if (string.IsNullOrEmpty(itemPrefab))
            {
                if (mount != null) mount.Detach(slot);
                return;
            }

            List<AnimationCacheService.ImageVariant> variants = AnimationCacheService.GetPrefabImageVariants(itemPrefab);
            if (variants == null)
                return;   // не image-prefab: визуала предмета нет

            // Спрайты всех вариантов из локального кеша (битый файл — пропуск с warning, не валим экип).
            var sources = new List<VariantSource>();
            foreach (AnimationCacheService.ImageVariant v in variants)
            {
                Sprite s;
                try { s = AnimationCacheService.TryGetSprite(BaseController.GAME_ID, v.File); }
                catch (System.Exception ex) { Debug.LogWarning("WeaponMount " + itemPrefab + " вариант " + v.angle + "°: " + ex.Message); Debug.LogException(ex); continue; }
                if (s == null) continue;
                sources.Add(new VariantSource { angle = v.angle, sprite = s, pivotX = v.pivotX, pivotY = v.pivotY });
            }
            if (sources.Count == 0)
                return;   // ни один вариант не загрузился

            if (mount == null) mount = wearer.gameObject.AddComponent<WeaponMount>();
            mount.Apply(slot, sources.ToArray(), AnimationCacheService.GetPrefabRotationMode(itemPrefab));
        }

        // Снять всё надетое (сервер прислал пустую экипировку — full-clear).
        public static void DetachAll(EntityModel wearer)
        {
            WeaponMount mount = wearer != null ? wearer.GetComponent<WeaponMount>() : null;
            if (mount == null)
                return;

            foreach (string slot in new List<string>(mount._slots.Keys))
                mount.Detach(slot);
        }

        // Надеть/обновить предмет в слоте: variants — все варианты картинки по направлениям (активный
        // выбирается по ракурсу тела), rotationMode — AnimationCacheService.RotationMode.* (mirror_x
        // добавляет зеркальных кандидатов). Якоря слота резолвятся отложенно (SpineAnchors).
        // Grip-спрайты (pivot = хват, 0..1, центр вращения) пересоздаются из текстур исходников один раз
        // здесь — не покадрово (Sprite.Create аллоцирует). Здесь же замеряется span варианта: у grip-спрайта
        // меш FullRect (прозрачные поля включены), поэтому видимые пиксели меряем по ИСХОДНИКУ из кеша — он
        // создан с мешом Tight, и tight-rect у него честный (тем же замером нормализуется предмет на земле).
        // Масштаб под span и рост носителя считает покадровая посадка — он зависит от активного якоря и варианта.
        private void Apply(string slot, VariantSource[] variants, string rotationMode)
        {
            Detach(slot);   // пересоздаём с нуля: освобождает прежние grip-спрайты (набор мог измениться)
            if (variants == null || variants.Length == 0)
                return;

            // Шейдер кусков — тот же, каким собраны страницы атласа самого скелета (SpineCacheService.Build):
            // предмет и тело обязаны рисоваться одинаково.
            Shader pieceShader = Shader.Find("Spine/Skeleton");
            if (pieceShader == null)
                Debug.LogWarning("WeaponMount " + slot + ": шейдера Spine/Skeleton нет — кусок предмета не собрать");

            Mounted m = new Mounted { slot = slot };
            m.rotationMode = rotationMode;
            m.variants = new Variant[variants.Length];
            for (int i = 0; i < variants.Length; i++)
            {
                Texture2D tex = variants[i].sprite.texture;
                Vector2 visible = AnimationCacheService.TryGetTightRect(variants[i].sprite, out Rect tight)
                    ? tight.size
                    : variants[i].sprite.bounds.size;
                var v = new Variant
                {
                    angle = variants[i].angle,
                    grip = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(variants[i].pivotX, variants[i].pivotY), Ppu, 0, SpriteMeshType.FullRect),
                    span = Mathf.Max(visible.x, visible.y),
                };
                // Кусок скелета берёт pivot спрайта своим началом координат (Sprite.ToAtlasRegion кладёт
                // его в offsetX/offsetY региона): доворот и зеркало идут вокруг ХВАТА, как у прежнего
                // SpriteRenderer, отдельной компенсации разницы «pivot ↔ центр куска» не нужно.
                if (pieceShader != null)
                {
                    v.grip.name = "WeaponMount_" + slot + "_" + v.angle;   // имя куска: пустое рантайм отвергает
                    v.material = new Material(pieceShader) { name = v.grip.name, mainTexture = tex };
                    v.piece = v.grip.ToRegionAttachment(v.material);
                }
                m.variants[i] = v;
            }
            // Посадка куска — покадрово (зависит от активного якоря и ракурса); покадровый проход идёт
            // до рендера, пустого первого кадра не будет.
            _slots[slot] = m;
        }

        public void Detach(string slot)
        {
            if (_slots.TryGetValue(slot, out Mounted m))
            {
                ClearHolder(m);
                Release(m);
                _slots.Remove(slot);
            }
        }

        // Сущность уничтожается (умерла, ушла из зоны видимости, мир перезагрузился) — вместе с ней уходит
        // и скелет, но grip-спрайты и материалы кусков созданы кодом и живут отдельно от сцены: без явного
        // Destroy они копятся в памяти на каждом снятом с карты существе.
        private void OnDestroy()
        {
            if (_skel != null) _skel.UpdateComplete -= OnSkeletonUpdate;

            foreach (Mounted m in _slots.Values)
                Release(m);

            _slots.Clear();
        }

        // Освободить объекты Unity, созданные кодом под варианты предмета.
        private void Release(Mounted m)
        {
            if (m.variants == null)
                return;

            foreach (Variant v in m.variants)
            {
                if (v == null) continue;
                if (v.grip != null) Destroy(v.grip);
                if (v.material != null) Destroy(v.material);
            }
        }


        // Экранное направление варианта картинки: нарисованный угол, зеркальный кандидат — 180−angle
        // (зеркало вокруг pivot'а). Одна формула на выбор кандидата (PickVariant) и на доворот принятого.
        private static float DrawnAngle(Variant v, bool flip)
            => flip ? Mathf.Repeat(180f - v.angle, 360f) : v.angle;

        // Выбор варианта картинки под экранный ракурс тела (fwdDeg). Экранный угол кандидата учитывает
        // зеркало ТЕЛА (флип корня h_mirror — предмет зеркалится вместе с телом, второй раз зеркалить
        // нельзя) и собственный флип предмета (только mirror_x: лево из права).
        // Побеждает кандидат с минимальной |DeltaAngle(fwdDeg, screen)|; для none/free flip-кандидатов нет.
        private static void PickVariant(Mounted m, float fwdDeg, bool mirrored, out int best, out bool flip)
        {
            best = 0; flip = false;
            float bestDist = float.MaxValue;
            int fMax = m.rotationMode == AnimationCacheService.RotationMode.MirrorX ? 1 : 0;
            for (int i = 0; i < m.variants.Length; i++)
            {
                for (int f = 0; f <= fMax; f++)
                {
                    float screen = DrawnAngle(m.variants[i], mirrored ^ (f == 1));
                    float d = Mathf.Abs(Mathf.DeltaAngle(fwdDeg, screen));
                    if (d < bestDist) { bestDist = d; best = i; flip = f == 1; }
                }
            }
        }

        // Экранный ракурс ТЕЛА — под него и выбирается вариант картинки, не под логический forward:
        // ракурсов может быть меньше, чем направлений (GetClipName «прилипает» к ближайшему клипу), и у
        // существа с единственным фронтальным видом тело смотрит вниз при любом forward — предмет обязан
        // следовать за телом. DisplayAngle — нарисованный угол играющего клипа; экранный ракурс = 180−angle
        // при зеркале корня. Forward — fallback: клип без направления либо резолв не удался.
        private float BodyAngle(bool mirrored)
        {
            int? bodyAngle = _em != null ? _em.DisplayAngle : null;
            if (bodyAngle.HasValue)
                return mirrored ? Mathf.Repeat(180f - bodyAngle.Value, 360f) : bodyAngle.Value;

            Vector3 fwd = _em != null ? _em.Forward : Vector3.right;
            if (fwd.x == 0f && fwd.y == 0f) fwd = Vector3.right;
            return Mathf.Atan2(fwd.y, fwd.x) * Mathf.Rad2Deg;
        }

        // Подписка на хук скелета — единственное, что делается покадрово отсюда: сама посадка предмета идёт
        // в хуке (OnSkeletonUpdate), потому что порядок LateUpdate между компонентами не определён, а
        // переставлять держателя в порядке отрисовки надо ПОСЛЕ того, как клип разложил свой, и ДО сборки меша.
        private void LateUpdate()
        {
            if (_slots.Count == 0) return;
            if (_em == null) _em = GetComponent<EntityModel>();

            BindSkeleton();
        }

        // Скелет собирается асинхронно и пересоздаётся при смене визуала (SpineVisualBuilder.Create сносит
        // прежний дочерний объект) — держим подписку по факту наличия объекта: уничтоженный скелет уносит
        // событие с собой, а Unity-оператор != null отдаёт по нему false, и мы находим пришедший на замену.
        private void BindSkeleton()
        {
            if (_skel != null) return;

            SkeletonAnimation found = GetComponentInChildren<SkeletonAnimation>();
            if (found == null) return;

            _skel = found;
            _skel.UpdateComplete += OnSkeletonUpdate;
        }

        // Хук самого скелета: клип уже разложен (в том числе его порядок отрисовки), мировые трансформы
        // костей посчитаны, меш ещё не собран — единственный момент кадра, где посадку предмета видно
        // рендеру целиком. UpdateComplete против UpdateWorld: второй заставил бы решать мировые трансформы
        // ЛИШНИЙ раз на каждой сущности с экипировкой.
        private void OnSkeletonUpdate(ISkeletonRenderer renderer)
        {
            Skeleton skeleton = renderer != null ? renderer.Skeleton : null;
            if (skeleton == null) return;

            // Мировых единиц на единицу скелета: тело нормировано в клетку масштабом своего объекта
            // (SpineVisualBuilder.Fit), и предмет считает свой размер в тех же единицах. Знак X — зеркало
            // тела: корень сущности флипает себя при смене направления, скелет наследует флип.
            Transform t = renderer.Component != null ? renderer.Component.transform : null;
            float unit = t != null ? Mathf.Abs(t.lossyScale.y) : 1f;
            if (unit < 0.0001f) unit = 1f;
            bool mirrored = t != null && t.lossyScale.x < 0f;

            foreach (Mounted m in _slots.Values)
                MountPiece(m, skeleton, unit, mirrored);
        }

        // Посадка куска предмета в слот-держатель скелета — раз в кадр на каждый надетый предмет.
        private void MountPiece(Mounted m, Skeleton skeleton, float unit, bool mirrored)
        {
            // Скелет сменился — прежние якоря, держатели и посадка кусков принадлежали ему.
            if (!ReferenceEquals(m.skeleton, _skel))
            {
                m.skeleton = _skel;
                m.anchors = null;
                m.holders = null;
                m.fallback = -1;
                m.tried = false;
                m.holder = null;
                if (m.variants != null)
                    foreach (Variant v in m.variants)
                        if (v != null) v.applied = false;
            }
            // Скелет на сущности есть — значит его пакет уже лежит на диске (им скелет и собран), и якоря
            // читаются с первой попытки. Повторять резолв незачем: следующий повод — смена скелета выше.
            // Держатели резолвим тем же разом: поиск слота по имени идёт перебором всего перечня, а
            // покадрово нужен сам слот, не его имя.
            if (!m.tried)
            {
                m.tried = true;
                m.anchors = SlotAnchors(m.slot);
                if (m.anchors != null)
                {
                    m.holders = new Slot[m.anchors.Length];
                    for (int i = 0; i < m.anchors.Length; i++)
                    {
                        string name = m.anchors[i].holder;
                        if (string.IsNullOrEmpty(name)) continue;   // якорь без держателя: ставить кусок некуда
                        m.holders[i] = skeleton.FindSlot(name);
                        if (m.holders[i] != null && m.fallback < 0) m.fallback = i;
                    }
                }
            }
            if (m.anchors == null || m.fallback < 0) { ClearHolder(m); return; }

            // Активный якорь — тот, в чьём ПОДДЕРЕВЕ костей висит надетая в этом кадре кожа: так скелет и
            // показывает ракурс — кожа одного ракурса надета, слоты прочих пусты. Поддерево, а не сама
            // кость: кость якоря своих слотов не несёт, она родитель костей, на которых кожа висит.
            // Ни одной такой — запасной якорь: у скелета без ракурсов выбирать не из чего.
            int idx = -1;
            foreach (Slot dressed in skeleton.Slots)
            {
                if (dressed.AppliedPose.Attachment == null) continue;
                idx = AnchorOf(m.holders, dressed.Bone);
                if (idx >= 0) break;
            }
            if (idx < 0) idx = m.fallback;

            SpineCacheService.SlotAnchor pick = m.anchors[idx];
            Slot holder = m.holders[idx];

            PickVariant(m, BodyAngle(mirrored), mirrored, out int vi, out bool flip);
            Variant variant = m.variants[vi];
            if (variant.piece == null) { ClearHolder(m); return; }

            BonePose bone = holder.Bone.AppliedPose;

            // Размер в руке НОРМИРУЕМ: длинная сторона видимых пикселей предмета = MountSpan × scale
            // якоря от роста носителя. Кусок живёт в системе координат
            // кости и унаследовал бы и масштаб тела (unit), и масштаб самой кости, — размер получался бы не
            // из данных, а из разрешения PNG и того, что анимация делает с рукой. Потому делим на оба.
            float boneScale = (bone.WorldScaleX + bone.WorldScaleY) * 0.5f;
            if (boneScale < 0.0001f) boneScale = 1f;
            float s = variant.span > 0.0001f
                ? MountSpan * pick.scale / (variant.span * boneScale * unit)
                : pick.scale;
            float sx = flip ? -s : s;   // зеркало вокруг pivot'а (хвата) — рукоять остаётся на кости

            // angle задан — предмет следует за костью: нарисованное направление варианта нормализуется к
            // канону (вправо), затем доворачивается на angle относительно кости. angle == null — «как
            // загружено»: снимаем поворот кости, поза предмета = его рисунок (копьё, нарисованное
            // вертикально, остаётся вертикальным). Зеркало тела живёт в масштабе объекта скелета и upright
            // не ломает.
            float rot = pick.angle.HasValue
                ? pick.angle.Value - DrawnAngle(variant, flip)
                : -bone.WorldRotationX;

            if (!variant.applied
                || Mathf.Abs(sx - variant.appliedScale) > Mathf.Abs(sx) * PieceScaleEpsilon
                || variant.appliedX != pick.offsetX || variant.appliedY != pick.offsetY
                || Mathf.Abs(Mathf.DeltaAngle(variant.appliedRot, rot)) > PieceRotEpsilon)
            {
                variant.piece.SetScale(sx, s);
                // Сдвиг от кости задан в пикселях исходной графики, и скелет записан в них же — единица
                // скелета есть пиксель, потому сдвиг кладётся как есть: к мировым единицам его приводит
                // масштаб объекта скелета, нормирующий тело в клетку.
                variant.piece.SetPositionOffset(pick.offsetX, pick.offsetY);
                variant.piece.SetRotation(rot);
                variant.piece.UpdateSequence();
                variant.applied = true;
                variant.appliedScale = sx;
                variant.appliedX = pick.offsetX;
                variant.appliedY = pick.offsetY;
                variant.appliedRot = rot;
            }

            if (!ReferenceEquals(m.holder, holder)) ClearHolder(m);
            m.holder = holder;
            if (holder.Pose.Attachment != variant.piece) holder.Pose.Attachment = variant.piece;

            PlaceHolder(skeleton, holder);
        }

        // Индекс якоря, в поддереве кости которого лежит названная кость. Идём от кости ВВЕРХ: держателей
        // у слота единицы, а предков у кости — считанные звенья.
        private static int AnchorOf(Slot[] holders, Bone bone)
        {
            for (Bone b = bone; b != null; b = b.Parent)
                for (int i = 0; i < holders.Length; i++)
                    if (holders[i] != null && ReferenceEquals(holders[i].Bone, b)) return i;

            return -1;
        }

        // Снять кусок с держателя: якорей не нашлось, держателя в скелете нет, активный якорь сменился на
        // другую кость, предмет снят. Держатели у сервера пустые — чужого в них не бывает.
        private static void ClearHolder(Mounted m)
        {
            if (m.holder != null) m.holder.Pose.Attachment = null;
            m.holder = null;
        }

        // Держатель — сразу ЗА последним слотом СВОЕГО поддерева костей: предмет поверх кожи своей руки и
        // под тем, что нарисовано дальше (голова над нагрудником). Сервер ставит держатели в КОНЕЦ перечня
        // слотов — оттуда предмет рисовался бы поверх всего. Порядок правим в том списке, который читает
        // сборка меша: при отсутствии ограничителей порядка он и есть общий, а при их наличии —
        // пересобирается из общего каждый кадр, и правка общего пропала бы. Кожи в поддереве нет
        // (last < 0) — держатель остаётся на месте, поверх всего.
        private static void PlaceHolder(Skeleton skeleton, Slot holder)
        {
            ExposedList<Slot> order = skeleton.DrawOrder.AppliedPose;
            Slot[] items = order.Items;
            Bone bone = holder.Bone;
            int at = -1, last = -1;
            for (int i = 0, n = order.Count; i < n; i++)
            {
                if (ReferenceEquals(items[i], holder)) { at = i; continue; }
                for (Bone b = items[i].Bone; b != null; b = b.Parent)
                    if (ReferenceEquals(b, bone)) { last = i; break; }
            }
            if (at < 0 || last < 0) return;

            // Снятие держателя сдвигает на единицу всё, что стояло за ним.
            int target = last < at ? last + 1 : last;
            if (target == at) return;

            order.RemoveAt(at);
            order.Insert(target, holder);
        }

        // Якоря слота на скелете носителя: сдвиг, доворот, множитель размера и имя слота-держателя.
        // Лежат в пакете скелета — читаются, когда скелет уже собран.
        private SpineCacheService.SlotAnchor[] SlotAnchors(string slot)
        {
            string wearerPrefab = _em != null ? _em.prefab : null;
            if (string.IsNullOrEmpty(wearerPrefab))
                return null;

            int animationId = AnimationCacheService.GetPrefabAnimation(wearerPrefab);
            string entity = AnimationCacheService.GetPrefabEntity(wearerPrefab);
            if (animationId == 0 || string.IsNullOrEmpty(entity))
                return null;

            Dictionary<string, List<SpineCacheService.SlotAnchor>> slots =
                SpineCacheService.GetSlots(BaseController.GAME_ID, animationId, entity);
            if (slots == null || !slots.TryGetValue(slot, out List<SpineCacheService.SlotAnchor> anchors)
                || anchors == null || anchors.Count == 0)
                return null;   // у скелета нет якорей этого слота

            return anchors.ToArray();
        }
    }
}
