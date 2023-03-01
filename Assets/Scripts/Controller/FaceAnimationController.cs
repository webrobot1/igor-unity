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
                if(value!=null)
                { 
                    if (value.animator != null)
                    {
                        animator.runtimeAnimatorController = value.animator.runtimeAnimatorController;
                    }
                    else
                        GetComponent<SpriteRenderer>().sprite = value.GetComponent<SpriteRenderer>().sprite;
                    lifeBar = value.GetComponentInChildren<CanvasGroup>();
                }

                _target = value;
            }
        }

        private CanvasGroup lifeBar;
        private Animator animator;
        private CanvasGroup targetFrame;

        private void Start()
        {
            targetFrame = GetComponentInParent<CanvasGroup>();
            if (targetFrame == null)
                PlayerController.Error("не наден фрейм жизней");

            animator = GetComponent<Animator>();
            if (animator == null)
                PlayerController.Error("не наден аниматор фрейма жизней");

            if (hpLine == null)
                PlayerController.Error("не надено изображения жизней фрейма");
            hpText = hpLine.GetComponentInChildren<Text>();


            if (mpLine == null)
                PlayerController.Error("не надено изображения жизней фрейма");
            mpText = mpLine.GetComponentInChildren<Text>();
        }

        private void Update()
        {
            if (target!=null)
            {
                if(target.hp!=null)
                    target.FillUpdate(hpLine, (float)target.hp, target.hpMax, hpText);

                if (target.mp != null)
                    target.FillUpdate(mpLine, (float)target.mp, target.mpMax, mpText);

                targetFrame.alpha = 1;

                if(lifeBar!=null)
                    lifeBar.alpha = 1;

                if (target.animator!=null && target.layerIndex!=null && target.layerIndex != layerIndex)
                {
                    target.Animate(animator, (int)target.layerIndex);
                    layerIndex = (int)target.layerIndex;
                }
            }
            else
            {
                targetFrame.alpha = 0;

                if (lifeBar != null)
                    lifeBar.alpha = 0;
            }      
        }
    }
}
