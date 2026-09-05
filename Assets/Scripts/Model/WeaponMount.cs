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
    // Место держателя в порядке отрисовки задаёт сам скелет: сервер ставит держатель сразу за слотом-якорем,
    // который выбрал человек.
    // Активен тот якорь, чья кость несёт ПОКАЗАННУЮ в этом кадре кожу — прямой аналог правила дерева
    // («точка присутствует в кадре»): ракурс скелет переключает тем, что кожа одного ракурса надета в свои
    // слоты, а прочие пусты. Кость якоря своих слотов не несёт — она РОДИТЕЛЬ костей, на которых висит кожа
    // ракурса, потому якорь ищется по её ПОДДЕРЕВУ.
    // Активного якоря в кадре нет — предмет не рисуется вовсе: ракурс, которого в этом кадре не видно,
    // предмета не несёт. То же правило держит страничный проигрыватель скелета (spine-pose-bridge.js).
    public class WeaponMount : MonoBehaviour
    {
        // Пикселей графики на единицу спрайта (как в AnimationCacheService.GetSprite). Той же величиной
        // масштабируется кусок предмета: единица скелета в наших пакетах — ПИКСЕЛЬ графики, а кусок собран
        // из спрайта с этим Ppu, потому натуральный размер картинки в скелете требует ровно такой поправки.
        private const float Ppu = 100f;

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
            public bool tried;
            public SkeletonAnimation skeleton;    // скелет, под который якоря резолвили: сменился — резолвим заново
            public Slot holder;                   // держатель, в котором сейчас стоит кусок
        }

        private readonly Dictionary<string, Mounted> _slots = new Dictionary<string, Mounted>();
        private SkeletonAnimation _skel;

        // Держатели ВСЕХ слотов экипировки скелета — по ним поиск активного якоря отличает их от кожи
        // ракурса. Набор общий на носителя (слоты экипировки резолвятся по одному пакету), потому лежит
        // у компонента, а не у надетого предмета.
        private HashSet<Slot> _holderSlots;
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
        // здесь — не покадрово (Sprite.Create аллоцирует). Меш у них FullRect: кусок несёт картинку целиком,
        // как её нарисовал художник, — прозрачные поля входят в размер наравне с рисунком.
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
                var v = new Variant
                {
                    angle = variants[i].angle,
                    grip = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                        new Vector2(variants[i].pivotX, variants[i].pivotY), Ppu, 0, SpriteMeshType.FullRect),
                };
                // Кусок скелета берёт pivot спрайта своим началом координат (Sprite.ToAtlasRegion кладёт
                // его в offsetX/offsetY региона): доворот и зеркало идут вокруг ХВАТА, отдельной
                // компенсации разницы «pivot ↔ центр куска» не нужно.
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
        // в хуке (OnSkeletonUpdate), потому что порядок LateUpdate между компонентами не определён, а посадка
        // считается по мировым трансформам костей — их скелет раскладывает у себя, и готовы они только к хуку.
        private void LateUpdate()
        {
            if (_slots.Count == 0) return;
            if (_em == null) _em = GetComponent<EntityModel>();

            BindSkeleton();
        }

        // Скелет собирается асинхронно и пересоздаётся при смене визуала (VisualBuilder.Create сносит
        // прежний дочерний объект) — держим подписку по факту наличия объекта: уничтоженный скелет уносит
        // событие с собой, а Unity-оператор != null отдаёт по нему false, и мы находим пришедший на замену.
        private void BindSkeleton()
        {
            if (_skel != null) return;

            SkeletonAnimation found = GetComponentInChildren<SkeletonAnimation>();
            if (found == null) return;

            _skel = found;
            _holderSlots = null;   // держатели принадлежали прежнему скелету
            _skel.UpdateComplete += OnSkeletonUpdate;
        }

        // Хук самого скелета: клип уже разложен, мировые трансформы костей посчитаны, меш ещё не собран —
        // единственный момент кадра, где посадку предмета видно рендеру целиком. UpdateComplete против
        // UpdateWorld: второй заставил бы решать мировые трансформы ЛИШНИЙ раз на каждой сущности с экипировкой.
        private void OnSkeletonUpdate(ISkeletonRenderer renderer)
        {
            Skeleton skeleton = renderer != null ? renderer.Skeleton : null;
            if (skeleton == null) return;

            // Знак X — зеркало тела: корень сущности флипает себя при смене направления, скелет наследует
            // флип, и вариант картинки выбирается уже с учётом этого (PickVariant).
            Transform t = renderer.Component != null ? renderer.Component.transform : null;
            bool mirrored = t != null && t.lossyScale.x < 0f;

            _holderSlots ??= HolderSlots(skeleton);

            foreach (Mounted m in _slots.Values)
                MountPiece(m, skeleton, mirrored);
        }

        // Посадка куска предмета в слот-держатель скелета — раз в кадр на каждый надетый предмет.
        private void MountPiece(Mounted m, Skeleton skeleton, bool mirrored)
        {
            // Скелет сменился — прежние якоря, держатели и посадка кусков принадлежали ему.
            if (!ReferenceEquals(m.skeleton, _skel))
            {
                m.skeleton = _skel;
                m.anchors = null;
                m.holders = null;
                m.tried = false;
                m.holder = null;
                if (m.variants != null)
                    foreach (Variant v in m.variants)
                        if (v != null) v.applied = false;
            }
            // Скелет на сущности есть — значит его пакет уже лежит на диске (им скелет и собран), и якоря
            // читаются с первой попытки. Повторять резолв незачем: следующий повод — смена скелета выше.
            // Держатели резолвим тем же разом: поиск слота по имени идёт перебором всего перечня, а
            // покадрово нужен сам слот, не его имя. Ни один держатель в скелете не нашёлся — слот остаётся
            // нерезолвнутым: ставить кусок некуда, и покадровый поиск активного якоря такому слоту не нужен.
            if (!m.tried)
            {
                m.tried = true;
                Dictionary<string, List<SpineCacheService.SlotAnchor>> slots = SkeletonSlots();
                if (slots != null && slots.TryGetValue(m.slot, out List<SpineCacheService.SlotAnchor> anchors)
                    && anchors != null && anchors.Count > 0)
                {
                    var holders = new Slot[anchors.Count];
                    bool any = false;
                    for (int i = 0; i < anchors.Count; i++)
                    {
                        string name = anchors[i].holder;
                        if (string.IsNullOrEmpty(name)) continue;   // якорь без держателя: ставить кусок некуда
                        holders[i] = skeleton.FindSlot(name);
                        any |= holders[i] != null;
                    }
                    if (any) { m.anchors = anchors.ToArray(); m.holders = holders; }
                }
            }
            if (m.anchors == null) { ClearHolder(m); return; }

            // Активный якорь — тот, в чьём ПОДДЕРЕВЕ костей висит надетая в этом кадре кожа: так скелет и
            // показывает ракурс — кожа одного ракурса надета, слоты прочих пусты. Поддерево, а не сама
            // кость: кость якоря своих слотов не несёт, она родитель костей, на которых кожа висит.
            // Держатели экипировки перебор пропускает: они не кожа, а место куска надетого предмета, и
            // сидят на кости СВОЕГО якоря — занятый держатель опознавался бы кожей своего же ракурса, и
            // предмет навсегда оставался бы на якоре, выбранном в первом кадре.
            // Ни одной такой — предмет в этом кадре не рисуется: клип, нарисованный вне костей ракурсов,
            // активного якоря не даёт вовсе.
            int idx = -1;
            foreach (Slot dressed in skeleton.Slots)
            {
                if (dressed.AppliedPose.Attachment == null || _holderSlots.Contains(dressed)) continue;
                idx = AnchorOf(m.holders, dressed.Bone);
                if (idx >= 0) break;
            }
            if (idx < 0) { ClearHolder(m); return; }

            SpineCacheService.SlotAnchor pick = m.anchors[idx];
            Slot holder = m.holders[idx];

            PickVariant(m, BodyAngle(mirrored), mirrored, out int vi, out bool flip);
            Variant variant = m.variants[vi];
            if (variant.piece == null) { ClearHolder(m); return; }

            // Кусок рисуется в НАТУРАЛЬНУЮ величину своей картинки: сколько в ней пикселей, столько единиц
            // скелета он и занимает. Поправка одна — Ppu: кусок собран из спрайта, где пиксели поделены на
            // Ppu (Apply), а единица скелета есть пиксель. Ни масштаб тела, ни масштаб кости отсюда не
            // вычитаются: предмет живёт внутри скелета и тянется вместе с ним и с рукой, как части тела.
            // Подгонку под конкретный слот и скелет задаёт множитель scale якоря (сервер).
            float s = Ppu * pick.scale;
            float sx = flip ? -s : s;   // зеркало вокруг pivot'а (хвата) — рукоять остаётся на кости

            // Предмет следует за костью: нарисованное направление варианта нормализуется к канону
            // (вправо), затем доворачивается на angle относительно кости.
            float rot = pick.angle - DrawnAngle(variant, flip);

            if (!variant.applied
                || Mathf.Abs(sx - variant.appliedScale) > Mathf.Abs(sx) * PieceScaleEpsilon
                || variant.appliedX != pick.offsetX || variant.appliedY != pick.offsetY
                || Mathf.Abs(Mathf.DeltaAngle(variant.appliedRot, rot)) > PieceRotEpsilon)
            {
                variant.piece.SetScale(sx, s);
                // Сдвиг от кости задан в пикселях исходной графики, и скелет записан в них же — единица
                // скелета есть пиксель, потому сдвиг кладётся как есть: к мировым единицам его, как и сам
                // кусок, приводит масштаб объекта скелета.
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

        // Снять кусок с держателя: якорей не нашлось, держателя в скелете нет, в этом кадре не активен ни
        // один якорь слота, активный якорь сменился на другую кость, предмет снят. Держатели у сервера
        // пустые — чужого в них не бывает.
        private static void ClearHolder(Mounted m)
        {
            if (m.holder != null) m.holder.Pose.Attachment = null;
            m.holder = null;
        }

        // Якоря экипировки скелета носителя: слот экипировки → его якоря (сдвиг, доворот, множитель
        // размера, имя слота-держателя). Лежат в пакете скелета — читаются, когда скелет уже собран.
        private Dictionary<string, List<SpineCacheService.SlotAnchor>> SkeletonSlots()
        {
            string wearerPrefab = _em != null ? _em.prefab : null;
            if (string.IsNullOrEmpty(wearerPrefab))
                return null;

            int animationId = AnimationCacheService.GetPrefabAnimation(wearerPrefab);
            string entity = AnimationCacheService.GetPrefabEntity(wearerPrefab);
            if (animationId == 0 || string.IsNullOrEmpty(entity))
                return null;

            return SpineCacheService.GetSlots(BaseController.GAME_ID, animationId, entity);
        }

        // Слоты-держатели скелета — по именам из якорей ВСЕХ слотов экипировки, не только надетого сейчас:
        // занят держатель любого слота, а от кожи ракурса поиск активного якоря обязан отличать их все.
        // Держателя нет в скелете — якорь пропускаем: ставить кусок туда всё равно некуда.
        private HashSet<Slot> HolderSlots(Skeleton skeleton)
        {
            var holders = new HashSet<Slot>();
            Dictionary<string, List<SpineCacheService.SlotAnchor>> slots = SkeletonSlots();
            if (slots == null)
                return holders;

            foreach (List<SpineCacheService.SlotAnchor> anchors in slots.Values)
            {
                if (anchors == null) continue;
                foreach (SpineCacheService.SlotAnchor anchor in anchors)
                {
                    if (string.IsNullOrEmpty(anchor.holder)) continue;
                    Slot holder = skeleton.FindSlot(anchor.holder);
                    if (holder != null) holders.Add(holder);
                }
            }

            return holders;
        }
    }
}
