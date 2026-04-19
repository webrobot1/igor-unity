using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    public class TargetController: MonoBehaviour
    {
        /// <summary>
        ///  скорость изменения полоски жизней и маны
        /// </summary>
        [SerializeField]
        private float lineSpeed = 3;
        [SerializeField]
        private float aspect = 30;
        [SerializeField]
        private Image hpLine;
        [SerializeField]
        private Image mpLine;        
        
        private Text hpText;
        private Text mpText;

        private Animator animator;
        private CanvasGroup targetFrame;
        private SpriteRenderer spriteRender;
        private Camera face_camera;

        /// <summary>
        ///  последняя воспроизведенная анимация
        /// </summary>
        public int _layerIndex;

        private ObjectModel _target = null;

        private void Awake()
        {
            targetFrame = GetComponentInParent<CanvasGroup>();
            animator = GetComponent<Animator>();
            face_camera = transform.parent.GetComponent<Camera>();
            spriteRender = transform.GetComponent<SpriteRenderer>();

            if (targetFrame == null)
                PlayerController.Error("не наден компонент CanvasGroup в блоке информации о цели");

            if (animator == null)
                PlayerController.Error("не наден компонент аниматор в блоке информации о цели");

            if (hpLine == null)
                PlayerController.Error("не присвоен компонент Image жизней в в блоке информации о цели");

            hpText = hpLine.GetComponentInChildren<Text>();

            if (mpLine == null)
                PlayerController.Error("не присвоен компонент Image маны в блоке информации о цели");

            mpText = mpLine.GetComponentInChildren<Text>();

            Target = null;
        }

        public ObjectModel Target
        {
            get 
            { 
                return _target; 
            }
            set
            {
                // ниже не даигать тк тут может быть null и мы повторно его присваиваем что бы скрыть заплатки тестовые
                // +  при переходе с севрера на сервер и объекты уничтожаясь НЕ вызвают тут set, но null будет стоять
                if (value == null)
                {
                    // не только скрыть но и позволить кликать по той области что бы ходить персонажем
                    targetFrame.alpha = 0;
                    //targetFrame.blocksRaycasts = false;
                }
                    

                if (_target != value)
                {
                    // если с прошлого существа есть
                    if (_target != null && ((EnemyModel)_target).lifeBar != null)
                    {
                        DisableLine(((EnemyModel)_target).lifeBar);
                    }

                    // снимаем любой ранее собранный Spriter-mirror с target-UI, прежде чем настраивать новую цель
                    NewSpriterRuntimeImporter.ClearMirror(gameObject);

                    _target = value;
                    if (value != null)
                    {
                        var localSr = GetComponent<SpriteRenderer>();

                        // value.animator.enabled == false когда цель использует Spriter
                        // (NewSpriterRuntimeImporter.CreateSpriter выключает legacy Animator и корневой SpriteRenderer).
                        if (value.animator != null && value.animator.enabled)
                        {
                            if (localSr != null) localSr.enabled = true;
                            animator.runtimeAnimatorController = value.animator.runtimeAnimatorController;
                            Animate();
                        }
                        else
                        {
                            animator.runtimeAnimatorController = null;
                            // чтобы FixedUpdate не кинул Animate() из-за рассинхрона слоя
                            _layerIndex = value.CurrentAnimationIndex;

                            var srcSpriter = value.GetComponent<SpriterDotNetUnity.SpriterDotNetBehaviour>();
                            if (srcSpriter != null && srcSpriter.SpriterData != null)
                            {
                                // Зеркалим Spriter-анимацию в target-UI, чтобы face_camera снимала её вживую.
                                // SpriteRenderer оставляем с последним корневым fallback-спрайтом (его bounds нужны CameraUpdate),
                                // но рендер выключаем — показывать будут Spriter-дети.
                                // SR у сущности теперь в child "Sprites" (UpdateController заворачивает его туда при спавне).
                                SpriteRenderer srcFallbackSr = value.GetComponentInChildren<SpriteRenderer>(true);
                                if (localSr != null)
                                {
                                    if (srcFallbackSr != null && srcFallbackSr.sprite != null)
                                        localSr.sprite = srcFallbackSr.sprite;
                                    localSr.enabled = false;
                                }
                                NewSpriterRuntimeImporter.MirrorFromSource(srcSpriter, gameObject);
                            }
                            else
                            {
                                // Статичный фолбэк для не-анимированных целей.
                                if (localSr != null) localSr.enabled = true;
                                SpriteRenderer spriteRender = value.GetComponentInChildren<SpriteRenderer>(true);
                                if (spriteRender == null)
                                    PlayerController.Error("На выбранном объекте налюдения присутвует колайдер но отсутвует Animator и SpriteRenderer");
                                if (localSr != null)
                                    localSr.sprite = spriteRender != null ? spriteRender.sprite : null;
                            }
                        }

                        EnemyModel enemyValue = value as EnemyModel;

                        // заполним поле жизней сразу
                        if (enemyValue.hp != null)
                        {
                            if (enemyValue.hp > 0)
                                EnableLine(hpLine);
                            else
                                DisableLine(hpLine);

                            FillUpdate(hpLine, (float)enemyValue.hp, enemyValue.hpMax, hpText, true);

                            if (enemyValue.lifeBar != null && (PlayerController.Player == null || value.key != PlayerController.Player.key))
                            {
                                if (enemyValue.hp > 0)
                                    EnableLine(enemyValue.lifeBar);

                                FillUpdate(enemyValue.lifeBar, (float)enemyValue.hp, enemyValue.hpMax, null, true);
                            }
                        }
                        else
                            DisableLine(hpLine); 

                        if (enemyValue.mp != null)
                        {
                            // ДА! Тоже завязан показ на жизни
                            if (enemyValue.mpMax>0 && ((enemyValue.hp != null && enemyValue.hp > 0) || (PlayerController.Player != null && _target.key == PlayerController.Player.key)))
                                EnableLine(mpLine);
                            else
                                DisableLine(mpLine);

                            FillUpdate(mpLine, (float)enemyValue.mp, enemyValue.mpMax, mpText, true);
                        }
                        else
                            DisableLine(mpLine); 

                        // покажем целиком верхнюю группу с анимациями      
                        targetFrame.alpha = 1;
                       // targetFrame.blocksRaycasts = true;
                    }
                }  
            }
        }

        private void EnableLine(Image line)
        {
            line.transform.parent.gameObject.SetActive(true);
        }

        public static void DisableLine(Image line)
        {
            line.transform.parent.gameObject.SetActive(false);
        }

        // если изображения анимации с сильно отличабщимеся pivot to возможно надо будет каждый FixedUpdate делать этот метод для пересчета положения камеры и объекта что бы он не выходил за рамки
        void CameraUpdate()
        {
            // Spriter-цель: есть child "Sprites" с N body-part'ами mirror-анимации.
            // Центрируем TargetAnimation по world-AABB mirror-спрайтов (иначе pivot fallback-спрайта смещает
            // камеру и видны только ноги) и ставим fov по честной перспективной формуле, учитывающей
            // фактическое расстояние от face-камеры до контента: fov_v = 2*atan(H / (2*D)) в градусах,
            // с margin'ом FRAME_MARGIN. Формула `aspect * size` (из оригинала) не масштабируется под
            // разные расстояния — здесь камера рядом с контентом, и прежняя формула давала крайности.
            // Content занимает ~1/FRAME_MARGIN ширины/высоты рамки. 1.25 → content ~80% рамки (небольшой воздух).
            const float FRAME_MARGIN = 1.25f;
            var spriterSprites = transform.Find("Sprites");
            if (spriterSprites != null)
            {
                Bounds agg = default;
                bool any = false;
                // Не фильтруем по sr.enabled / gameObject.activeInHierarchy: UnityAnimator иногда на один кадр
                // оставляет включёнными только "активные в анимации" тел-детали, и агрегат получается пустой.
                // sr.sprite != null — достаточная проверка что это Spriter body-part, который участвует в компоновке.
                foreach (var sr in spriterSprites.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (sr == null || sr.sprite == null) continue;
                    // sr.bounds = AABB меша в мировых координатах с текущим transform'ом.
                    // Если SR выключен — bounds.size=0 у некоторых версий Unity; skip, чтобы не исказить.
                    Bounds srB = sr.bounds;
                    if (srB.size.sqrMagnitude < 0.0001f) continue;
                    if (!any) { agg = srB; any = true; }
                    else agg.Encapsulate(srB);
                }
                if (any)
                {
                    // Смещаем TargetAnimation так, чтобы центр mirror-AABB стал в точке камеры (parent.position).
                    Vector3 localCenter = transform.parent.InverseTransformPoint(agg.center);
                    Vector3 lp = transform.localPosition;
                    transform.localPosition = new Vector3(lp.x - localCenter.x, lp.y - localCenter.y, 1);

                    // Расстояние камера→контент в WORLD-юнитах (проекция camToContent на cam.forward).
                    // InverseTransformPoint не подходит: у face-камеры lossyScale ~(0.24, 0.25, 0.46) из-за UI canvas,
                    // local-z не равен world-z. Unity-проекция использует world-distance.
                    Vector3 camToContent = agg.center - face_camera.transform.position;
                    float worldDistance = Mathf.Abs(Vector3.Dot(camToContent, face_camera.transform.forward));
                    if (worldDistance < 0.01f) worldDistance = 0.01f;
                    // Margin применяем к целевому видимому размеру (линейно), а не к углу (tan нелинейный).
                    // Нужный вертикальный fov: fov = 2 * atan(H_margin / (2 * D)).
                    float targetH = agg.size.y * FRAME_MARGIN;
                    float targetW = agg.size.x * FRAME_MARGIN;
                    float needV = 2f * Mathf.Atan(targetH / (2f * worldDistance)) * Mathf.Rad2Deg;
                    // Для ширины: tan(fovH/2) = cam.aspect * tan(fovV/2) →  fovV_fromW = 2*atan( (W/aspect) / (2D) ).
                    float camAspect = face_camera.aspect > 0.01f ? face_camera.aspect : 1f;
                    float needVFromW = 2f * Mathf.Atan(targetW / (camAspect * 2f * worldDistance)) * Mathf.Rad2Deg;
                    face_camera.fieldOfView = Mathf.Clamp(Mathf.Max(needV, needVFromW), 1f, 179f);
                    return;
                }
            }

            // Non-Spriter (статичный fallback-спрайт, warrior для player, unknow для объектов без scml):
            // формула ниже сместит изображение выделелнного предмета так что бы оно оставалось в центре (смещаться будет если pivot отличается от (0.5, 0.5) )
            Bounds bounds = spriteRender.sprite.bounds;
            Vector2 vector = new Vector2(-bounds.center.x / bounds.extents.x / 2, -bounds.center.y / bounds.extents.y / 2);
            transform.localPosition = new Vector3(vector.x * (spriteRender.sprite.rect.size.x / spriteRender.sprite.pixelsPerUnit) * transform.localScale.y, vector.y * (spriteRender.sprite.rect.size.y / spriteRender.sprite.pixelsPerUnit) * transform.localScale.y, 1);

            float max = Mathf.Max(spriteRender.sprite.rect.size.x / spriteRender.sprite.pixelsPerUnit, spriteRender.sprite.rect.size.y / spriteRender.sprite.pixelsPerUnit);

            // эта пропорция изменит отдаленность камерыб число aspect  это контента которая была выситана с учетом размера окна на 1х1 unit размера изображения умножается как раз на юниты размера (считаюстя как текущий размер деленый на pixelsPerUnit)
            // face_camera.orthographicSize = aspect * max;
            face_camera.fieldOfView = aspect * max;
        }

        private void FixedUpdate()
        {
            if (_target != null)
            {
                CameraUpdate();

                // если ушли слишком далеко от существа уберем его как цель
                if (PlayerController.Player == null || (_target.key != PlayerController.Player.key && Vector3.Distance(PlayerController.Player.transform.position, _target.transform.position) >= PlayerController.Player.lifeRadius))
                {
                    Target = null;
                    return;
                }
                    
                if (_target.animator != null && _target.CurrentAnimationIndex != _layerIndex)
                {
                    Animate();
                }

                EnemyModel enemyTarget = _target as EnemyModel;

                if (enemyTarget.hp != null)
                {
                    if (enemyTarget.hp > 0 || (PlayerController.Player != null && _target.key == PlayerController.Player.key))
                        EnableLine(hpLine);
                    else
                        DisableLine(hpLine);

                    FillUpdate(hpLine, (float)enemyTarget.hp, enemyTarget.hpMax, hpText);

                    if (enemyTarget.lifeBar != null && (PlayerController.Player == null || enemyTarget.key != PlayerController.Player.key))
                    {
                        if (enemyTarget.hp>0)
                            EnableLine(enemyTarget.lifeBar); 
                        else
                            DisableLine(enemyTarget.lifeBar);

                        FillUpdate(enemyTarget.lifeBar, (float)enemyTarget.hp, enemyTarget.hpMax);
                    }     
                }
                    
                if (enemyTarget.mp!=null)
                {
                    // ДА! Тоже завязан показ на жизни
                    if (enemyTarget.mpMax>0 && ((enemyTarget.hp!=null && enemyTarget.hp>0) || (PlayerController.Player != null && enemyTarget.key == PlayerController.Player.key)))
                        EnableLine(mpLine);
                    else
                        DisableLine(mpLine);

                    FillUpdate(mpLine, (float)enemyTarget.mp, enemyTarget.mpMax, mpText);
                }
            }     
        }

        private void FillUpdate(Image line, float current, float max, Text text = null, bool force = false)
        {
            float newFill = current / max;
            if (newFill != line.fillAmount || force) //If we have a new fill amount then we know that we need to update the bar
            {
                if (force)
                    line.fillAmount = newFill;
                else
                    line.fillAmount = Mathf.Lerp(line.fillAmount, newFill, Time.deltaTime * lineSpeed);

                // текст обновляем всегда сразу, без lerp
                if (text != null)
                    text.text = current + " / " + max;
            }
            // при force=false текст может не обновиться если fillAmount уже совпал, но значения изменились
            else if (text != null)
            {
                string newText = current + " / " + max;
                if (text.text != newText)
                    text.text = newText;
            }
        }

        private void Animate()
        {
            _layerIndex = _target.CurrentAnimationIndex;
            _target.Animate(animator, _target.CurrentAnimationIndex);
        }
    }
}
