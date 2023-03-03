using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MyFantasy
{
    public class FaceAnimationController: MonoBehaviour
    {
        [SerializeField]
        private float aspect = 30;
        [SerializeField]
        private Image hpLine;
        [SerializeField]
        private Image mpLine;        
        

        private Text hpText;
        private Text mpText;

        private CanvasGroup lifeBar;
        private Animator animator;
        private CanvasGroup targetFrame;
        private SpriteRenderer spriteRender;
        private Camera face_camera;

        /// <summary>
        ///  активный слой анимации
        /// </summary>
        [NonSerialized]
        public int layerIndex;

        private NewObjectModel _target;
        public NewObjectModel target
        {
            get { 
                return _target; 
            }
            set 
            {
                if (_target != value)
                {
                    // если с прошлого существа есть 
                    if (_target != null && lifeBar != null)
                        lifeBar.alpha = 0;

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
                            value.FillUpdate(hpLine, (float)value.hp, value.hpMax, hpText, true);

                            lifeBar = value.GetComponentInChildren<CanvasGroup>();
                            if (lifeBar != null && value.hp > 0)
                                lifeBar.alpha = 1;
                        }
                        else
                            hpLine.transform.parent.gameObject.SetActive(false);

                        if (value.mp != null)
                        {
                            value.FillUpdate(mpLine, (float)value.mp, value.mpMax, mpText, true);
                        }
                        else
                            mpLine.transform.parent.gameObject.SetActive(false);

                        
                        targetFrame.alpha = 1;
                    }
                    else
                        targetFrame.alpha = 0;
                }  
            }
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
                if (target.hp != null)
                {
                    target.FillUpdate(hpLine, (float)target.hp, target.hpMax, hpText);
                    if (lifeBar != null)
                    {
                        if (target.hp==0) 
                            lifeBar.alpha = 0;
                        else 
                            lifeBar.alpha = 1;
                    }     
                }
                    
                if (target.mp != null)
                    target.FillUpdate(mpLine, (float)target.mp, target.mpMax, mpText);

                if (target.animator!=null && target.layerIndex != layerIndex)
                {
                    Animate();
                }
            }     
        }

        private void Animate()
        {
            target.Animate(animator, target.layerIndex);
            layerIndex = target.layerIndex;
        }
    }
}
