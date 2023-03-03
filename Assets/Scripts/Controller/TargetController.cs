using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MyFantasy
{
    public class TargetController: MonoBehaviour
    {
        [SerializeField]
        private float aspect = 30;
        [SerializeField]
        private Image hpLine;
        [SerializeField]
        private Image mpLine;        
        

        private Text hpText;
        private Text mpText;
        private Image lifeBarHp;

        private Animator animator;
        private CanvasGroup targetFrame;
        private SpriteRenderer spriteRender;
        private Camera face_camera;

        /// <summary>
        ///  активный слой анимации
        /// </summary>
        [NonSerialized]
        public int layerIndex;

        /// <summary>
        ///  скорость изменения полоски жизней и маны
        /// </summary>
        private static float lineSpeed = 3;

        private NewObjectModel _target;
        public NewObjectModel target
        {
            get { 
                return _target; 
            }
            set 
            {
                // ниже не даигать тк тут может быть null и мы повторно его присваиваем что бы скрыть заплатки тестовые
                // +  при переходе с севрера на сервер и объекты уничтожаясь НЕ вызвают тут set, но null будет стоять
                if(value == null) 
                    targetFrame.alpha = 0;

                if (_target != value)
                {
                    // если с прошлого существа есть 
                    if (_target != null && _target.lifeBar != null)
                    {
                        DisableLine(_target.lifeBar);
                    }

                    _target = value;
                    if (value != null)
                    {
                        if (value.animator != null)
                        {
                            animator.runtimeAnimatorController = value.animator.runtimeAnimatorController;
                            Animate();
                        }
                        else 
                        {
                            SpriteRenderer spriteRender = value.GetComponent<SpriteRenderer>();
                            if (spriteRender == null)
                                PlayerController.Error("На выбранном объекте налюдения присутвует колайдер но отсутвует Animator и SpriteRenderer");

                            animator.runtimeAnimatorController = null;
                            GetComponent<SpriteRenderer>().sprite = value.GetComponent<SpriteRenderer>().sprite;
                        }

                        // заполним поле жизней сразу
                        if (value.hp != null)
                        {
                            if (value.hp > 0)
                                EnableLine(hpLine);
                            else
                                DisableLine(hpLine);

                            FillUpdate(hpLine, (float)value.hp, value.hpMax, hpText, true);

                            if (value.lifeBar != null && (PlayerController.Instance.player == null || value.key != PlayerController.Instance.player.key))
                            {
                                if (value.hp > 0)
                                    EnableLine(value.lifeBar);
                                FillUpdate(value.lifeBar, (float)value.hp, value.hpMax, null, true);
                            }
                        }
                        else
                            DisableLine(hpLine); 

                        if (value.mp != null)
                        {
                            // ДА! Тоже завязан показ на жизни
                            if (value.mpMax>0 && ((value.hp != null && value.hp > 0) || (PlayerController.Instance.player != null && target.key == PlayerController.Instance.player.key)))
                                EnableLine(mpLine);
                            else
                                DisableLine(mpLine);

                            FillUpdate(mpLine, (float)value.mp, value.mpMax, mpText, true);
                        }
                        else
                            DisableLine(mpLine); 

                        // покажем целиком верхнюю группу с анимациями      
                        targetFrame.alpha = 1;
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

        private void Awake()
        {
            targetFrame = GetComponentInParent<CanvasGroup>();
            animator = GetComponent<Animator>();
            face_camera = transform.parent.GetComponent<Camera>();
            spriteRender = transform.GetComponent<SpriteRenderer>();
        }

        private void Start()
        {        
            if (targetFrame == null)
                PlayerController.Error("не наден фрейм жизней");

            if (animator == null)
                PlayerController.Error("не наден аниматор фрейма жизней");

            if (hpLine == null)
                PlayerController.Error("не надено изображения жизней фрейма");
            hpText = hpLine.GetComponentInChildren<Text>();

            if (mpLine == null)
                PlayerController.Error("не надено изображения жизней фрейма");
            mpText = mpLine.GetComponentInChildren<Text>();
        }

        // если изображения анимации с сильно отличабщимеся pivot to возможно надо будет каждый FixedUpdate делать этот метод для пересчета положения камеры и объекта что бы он не выходил за рамки
        void CameraUpdate()
        {
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
            if (target!=null)
            {
                CameraUpdate();
                if (PlayerController.Instance.player == null || (target.key != PlayerController.Instance.player.key && Vector3.Distance(PlayerController.Instance.player.transform.position, target.transform.position) >= PlayerController.Instance.player.lifeRadius))
                    target = null;

                if (target.hp != null)
                {
                    if (target.hp > 0 || (PlayerController.Instance.player != null && target.key == PlayerController.Instance.player.key))
                        EnableLine(hpLine);
                    else
                        DisableLine(hpLine);

                    FillUpdate(hpLine, (float)target.hp, target.hpMax, hpText);

                    if (target.lifeBar != null && (PlayerController.Instance.player == null || target.key != PlayerController.Instance.player.key))
                    {
                        if (target.hp>0)
                            EnableLine(target.lifeBar); 
                        else
                            DisableLine(target.lifeBar);

                        FillUpdate(target.lifeBar, (float)target.hp, target.hpMax);
                    }     
                }
                    
                if (target.mp!=null)
                {
                    // ДА! Тоже завязан показ на жизни
                    if (target.mpMax>0 && ((target.hp!=null && target.hp>0) || (PlayerController.Instance.player != null && target.key == PlayerController.Instance.player.key)))
                        EnableLine(mpLine);
                    else
                        DisableLine(mpLine);

                    FillUpdate(mpLine, (float)target.mp, target.mpMax, mpText);
                }
                   
                if (target.animator!=null && target.layerIndex != layerIndex)
                {
                    Animate();
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

                if (text != null)
                    text.text = current + " / " + max;
            }
        }

        private void Animate()
        {
            target.Animate(animator, target.layerIndex);
            layerIndex = target.layerIndex;
        }
    }
}
