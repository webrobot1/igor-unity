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
        ///  �������� ��������� ������� ������ � ����
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
        ///  ��������� ���������������� ��������
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
                PlayerController.Error("�� ����� ��������� CanvasGroup � ����� ���������� � ����");

            if (animator == null)
                PlayerController.Error("�� ����� ��������� �������� � ����� ���������� � ����");

            if (hpLine == null)
                PlayerController.Error("�� �������� ��������� Image ������ � � ����� ���������� � ����");

            hpText = hpLine.GetComponentInChildren<Text>();

            if (mpLine == null)
                PlayerController.Error("�� �������� ��������� Image ���� � ����� ���������� � ����");

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
                // ���� �� ������� �� ��� ����� ���� null � �� �������� ��� ����������� ��� �� ������ �������� ��������
                // +  ��� �������� � ������� �� ������ � ������� ����������� �� ������� ��� set, �� null ����� ������
                if (value == null)
                {
                    // �� ������ ������ �� � ��������� ������� �� ��� ������� ��� �� ������ ����������
                    targetFrame.alpha = 0;
                    //targetFrame.blocksRaycasts = false;
                }
                    

                if (_target != value)
                {
                    // ���� � �������� �������� ���� 
                    if (_target != null && ((EnemyModel)_target).lifeBar != null)
                    {
                        DisableLine(((EnemyModel)_target).lifeBar);
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
                                PlayerController.Error("�� ��������� ������� ��������� ���������� �������� �� ��������� Animator � SpriteRenderer");

                            animator.runtimeAnimatorController = null;
                            GetComponent<SpriteRenderer>().sprite = value.GetComponent<SpriteRenderer>().sprite;
                        }

                        EnemyModel enemyValue = value as EnemyModel;

                        // �������� ���� ������ �����
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
                            // ��! ���� ������� ����� �� �����
                            if (enemyValue.mpMax>0 && ((enemyValue.hp != null && enemyValue.hp > 0) || (PlayerController.Player != null && _target.key == PlayerController.Player.key)))
                                EnableLine(mpLine);
                            else
                                DisableLine(mpLine);

                            FillUpdate(mpLine, (float)enemyValue.mp, enemyValue.mpMax, mpText, true);
                        }
                        else
                            DisableLine(mpLine); 

                        // ������� ������� ������� ������ � ����������      
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

        // ���� ����������� �������� � ������ ������������� pivot to �������� ���� ����� ������ FixedUpdate ������ ���� ����� ��� ��������� ��������� ������ � ������� ��� �� �� �� ������� �� �����
        void CameraUpdate()
        {
            // ������� ���� ������� ����������� ������������ �������� ��� ��� �� ��� ���������� � ������ (��������� ����� ���� pivot ���������� �� (0.5, 0.5) )
            Bounds bounds = spriteRender.sprite.bounds;
            Vector2 vector = new Vector2(-bounds.center.x / bounds.extents.x / 2, -bounds.center.y / bounds.extents.y / 2);
            transform.localPosition = new Vector3(vector.x * (spriteRender.sprite.rect.size.x / spriteRender.sprite.pixelsPerUnit) * transform.localScale.y, vector.y * (spriteRender.sprite.rect.size.y / spriteRender.sprite.pixelsPerUnit) * transform.localScale.y, 1);

            float max = Mathf.Max(spriteRender.sprite.rect.size.x / spriteRender.sprite.pixelsPerUnit, spriteRender.sprite.rect.size.y / spriteRender.sprite.pixelsPerUnit);

            // ��� ��������� ������� ������������ ������� ����� aspect  ��� �������� ������� ���� �������� � ������ ������� ���� �� 1�1 unit ������� ����������� ���������� ��� ��� �� ����� ������� (��������� ��� ������� ������ ������� �� pixelsPerUnit)
            // face_camera.orthographicSize = aspect * max;
            face_camera.fieldOfView = aspect * max;
        }

        private void FixedUpdate()
        {
            if (_target != null)
            {
                CameraUpdate();

                // ���� ���� ������� ������ �� �������� ������ ��� ��� ����
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
                    // ��! ���� ������� ����� �� �����
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

                if (text != null)
                    text.text = current + " / " + max;
            }
        }

        private void Animate()
        {
            _layerIndex = _target.CurrentAnimationIndex;
            _target.Animate(animator, _target.CurrentAnimationIndex);
        }
    }
}
