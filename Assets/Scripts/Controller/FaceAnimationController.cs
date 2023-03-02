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

        private CanvasGroup lifeBar;
        private Animator animator;
        private CanvasGroup targetFrame;
        private SpriteRenderer spriteRender;
        private Camera face_camera;

        /// <summary>
        ///  �������� ���� ��������
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
                // ���� � �������� �������� ���� 
                if (_target != null && lifeBar != null)
                    lifeBar.alpha = 0;

                if (value != null)
                {
                    if (_target != value)
                    {
                        if (value.animator != null)
                        {
                            animator.runtimeAnimatorController = value.animator.runtimeAnimatorController;
                        }
                        else 
                        {
                            SpriteRenderer spriteRender = value.GetComponent<SpriteRenderer>();
                            if (spriteRender == null)
                                PlayerController.Error("�� ��������� ������� ��������� ���������� �������� �� ��������� Animator � SpriteRenderer");

                            animator.runtimeAnimatorController = null;
                            GetComponent<SpriteRenderer>().sprite = value.GetComponent<SpriteRenderer>().sprite;
                        }

                        // �������� ���� ������ �����
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
                }
                else
                    targetFrame.alpha = 0;


                _target = value;
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
                PlayerController.Error("�� ����� ����� ������");

            if (animator == null)
                PlayerController.Error("�� ����� �������� ������ ������");

            if (hpLine == null)
                PlayerController.Error("�� ������ ����������� ������ ������");
            hpText = hpLine.GetComponentInChildren<Text>();

            if (mpLine == null)
                PlayerController.Error("�� ������ ����������� ������ ������");
            mpText = mpLine.GetComponentInChildren<Text>();
        }

        void CameraUpdate()
        {
            // ������� ���� ������� ����������� ������������ �������� ��� ��� �� ��� ���������� � ������ (��������� ����� ���� pivot ���������� �� (0.5, 0.5) )
            Bounds bounds = spriteRender.sprite.bounds;
            Vector2 vector = new Vector2(-bounds.center.x / bounds.extents.x / 2, -bounds.center.y / bounds.extents.y / 2);
            transform.localPosition = new Vector3(vector.x * (spriteRender.sprite.rect.size.x / spriteRender.sprite.pixelsPerUnit) * transform.localScale.y, vector.y * (spriteRender.sprite.rect.size.y / spriteRender.sprite.pixelsPerUnit) * transform.localScale.y, 1);

            float max = Mathf.Max(spriteRender.sprite.rect.size.x / spriteRender.sprite.pixelsPerUnit, spriteRender.sprite.rect.size.y / spriteRender.sprite.pixelsPerUnit);

            // ��� ��������� ������� ������������ ������� ����� 0.15f  ��� �������� ������� ���� �������� � ������ ������� ���� �� 1�1 unit ������� ����������� ���������� ��� ��� �� ����� ������� (��������� ��� ������� ������ ������� �� pixelsPerUnit)
            // face_camera.orthographicSize = 0.15f * max;
            face_camera.fieldOfView = 26.136f * max;

            //Debug.Log(1.78f-(float)Screen.width / (float)Screen.height);
            //Debug.Log(face_camera.aspect);
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

                if (target.animator!=null && target.layerIndex!=null && target.layerIndex != layerIndex)
                {
                    target.Animate(animator, (int)target.layerIndex);
                    layerIndex = (int)target.layerIndex;
                }
            }     
        }
    }
}
