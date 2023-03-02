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
        private Image hpLine;
        [SerializeField]
        private Image mpLine;        
        

        private Text hpText;
        private Text mpText;

        /// <summary>
        ///  активный слой анимации
        /// </summary>
        [NonSerialized]
        public int layerIndex;

        private NewEnemyModel _target;
        public NewEnemyModel target
        {
            get { 
                return _target; 
            }
            set 
            {
                if (lifeBar != null)
                    lifeBar.alpha = 0;

                targetFrame.alpha = 0;

                if (value != null)
                {
                    if (_target != value)
                    {
                        if (value.animator != null)
                        {
                            animator.runtimeAnimatorController = value.animator.runtimeAnimatorController;
                        }
                        else
                            GetComponent<SpriteRenderer>().sprite = value.GetComponent<SpriteRenderer>().sprite;

                        // заполним поле жизней сразу
                        if (value.hp != null)
                            value.FillUpdate(hpLine, (float)value.hp, value.hpMax, hpText, true);

                        if (value.mp != null)
                            value.FillUpdate(mpLine, (float)value.mp, value.mpMax, mpText, true);

                        lifeBar = value.GetComponentInChildren<CanvasGroup>();
                    }
                    if (lifeBar != null)
                        lifeBar.alpha = 1;

                    targetFrame.alpha = 1;
                }

                _target = value;
            }
        }

        private CanvasGroup lifeBar;
        private Animator animator;
        private CanvasGroup targetFrame;

        private void Awake()
        {
            targetFrame = GetComponentInParent<CanvasGroup>();
            animator = GetComponent<Animator>();
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

        private void FixedUpdate()
        {
            if (target!=null)
            {
                if(target.hp!=null)
                    target.FillUpdate(hpLine, (float)target.hp, target.hpMax, hpText);

                if (target.mp != null)
                    target.FillUpdate(mpLine, (float)target.mp, target.mpMax, mpText);

                if (target.animator!=null && target.layerIndex!=null && target.layerIndex != layerIndex)
                {
                    target.Animate(animator, (int)target.layerIndex);
                    layerIndex = (int)target.layerIndex;
                }
            }     
        }
    }
}
