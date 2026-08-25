using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Mmogick
{
    /// <summary>
    /// Рамка выбранной цели: портрет сущности (общая механика — <see cref="EntityPortrait"/>) плюс её
    /// состояние — полоски жизней и маны, имя. Та же рамка обслуживает и самого игрока (см. PlayerController).
    /// </summary>
    public class TargetController: EntityPortrait
    {
        /// <summary>
        ///  скорость изменения полоски жизней и маны
        /// </summary>
        [SerializeField]
        private float lineSpeed = 3;
        [SerializeField]
        private Image hpLine;
        [SerializeField]
        private Image mpLine;

        /// <summary>
        /// Имя того, кто показан в рамке: в рамке игрока — его собственный логин (та же рамка обслуживает
        /// и игрока, и цель — см. PlayerController), в рамке цели — имя выбранного существа.
        /// </summary>
        [SerializeField]
        private Text nameLabel;

        private Text hpText;
        private Text mpText;

        private CanvasGroup targetFrame;

        protected override void Awake()
        {
            base.Awake();

            targetFrame = GetComponentInParent<CanvasGroup>();

            // Error() не бросает: без return следующая строка разыменовала бы тот же null,
            // и вместо внятной жалобы игрок получал бы NRE.
            if (targetFrame == null)
            {
                PlayerController.Error("не наден компонент CanvasGroup в блоке информации о цели");
                return;
            }

            if (hpLine == null)
            {
                PlayerController.Error("не присвоен компонент Image жизней в в блоке информации о цели");
                return;
            }

            hpText = hpLine.GetComponentInChildren<Text>();

            if (mpLine == null)
            {
                PlayerController.Error("не присвоен компонент Image маны в блоке информации о цели");
                return;
            }

            mpText = mpLine.GetComponentInChildren<Text>();

            if (nameLabel == null)
            {
                PlayerController.Error("не присвоен Text имени в блоке информации о цели");
                return;
            }

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

                    // Имя гаснет вместе с рамкой: показанное имя без рамки читалось бы как живая цель.
                    // Пока цель есть, имя ведёт FixedUpdate — оно приходит с сервера отдельным пакетом
                    // и у своего игрока встаёт позже, чем рамка получает его самого.
                    if (nameLabel != null) nameLabel.text = "";
                }


                // Сравнение ссылочное, не Unity-оператором: уничтоженная цель равна null ПО UNITY, и по
                // Unity-сравнению сброс `Target = null` выглядел бы «значение не изменилось» — рамка,
                // имя и зеркало анимации прежней цели остались бы висеть.
                if (!ReferenceEquals(_target, value))
                {
                    // Полоска жизни над прежней целью гаснет вместе со снятием выбора. Целью бывает и не
                    // существо (сундук, лавка, портал, лежащая вещь) — своей полоски у такой цели нет.
                    EnemyModel previous = _target as EnemyModel;

                    if (previous != null && previous.lifeBar != null)
                    {
                        DisableLine(previous.lifeBar);
                    }

                    _target = value;

                    // Портрет собирается общей механикой: зеркало тела (скелет либо дерево) либо кадр неанимированной
                    // цели. Снятие прежнего зеркала — там же.
                    ApplyVisual(value);

                    if (value != null)
                    {
                        // Целью бывает не только существо: сундук, лавка, портал, лежащая вещь — у них
                        // ни запаса здоровья, ни маны, и полоскам показывать нечего.
                        EnemyModel enemyValue = value as EnemyModel;

                        if (enemyValue == null)
                        {
                            DisableLine(hpLine);
                            DisableLine(mpLine);
                            targetFrame.alpha = 1;
                            return;
                        }

                        // заполним поле жизней сразу
                        UpdateHpMpLines(enemyValue, true);

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

        private void FixedUpdate()
        {
            // Цель исчезла с карты сама (умерла, ушла, мир перезагрузился): объект уничтожен, а сеттер
            // никто не звал — Unity-ссылка стала мёртвой без присвоения. Признак тот же, которым гаснет
            // иконка сведений у рамки (InfoWindowController), и рамка обязана сниматься вместе с ней:
            // иначе имя и портрет прежней цели остаются на экране как живая цель.
            if (_target == null)
            {
                if (!ReferenceEquals(_target, null))
                    Target = null;

                return;
            }

            if (nameLabel != null)
                nameLabel.text = _target.DisplayName;

            // Портрет: поздняя привязка тела, кадр статичной цели, зеркало анимации и наводка камеры.
            PortraitUpdate();

            // если ушли слишком далеко от существа уберем его как цель
            if (PlayerController.Player == null || (_target.key != PlayerController.Player.key && Vector3.Distance(PlayerController.Player.transform.position, _target.transform.position) >= PlayerController.Player.lifeRadius))
            {
                Target = null;
                return;
            }

            EnemyModel enemyTarget = _target as EnemyModel;

            // Цель не существо (сундук, лавка, портал, лежащая вещь) — рассказывать полоскам нечего,
            // остаётся имя и портрет.
            if (enemyTarget == null)
            {
                DisableLine(hpLine);
                DisableLine(mpLine);
                return;
            }

            UpdateHpMpLines(enemyTarget, false);
        }

        /// <summary>
        /// Полоски HP/MP текущей цели: общий код для входа цели (<see cref="Target"/>, force=true — сразу,
        /// без плавности) и каждого кадра (<see cref="FixedUpdate"/>, force=false — с lerp через <see cref="FillUpdate"/>).
        ///
        /// Своего игрока (own) полоска здоровья и маны не гасит при hp=0: труп-раннер продолжает двигаться
        /// полупрозрачным призраком (см. PlayerModel), и рамка обязана показывать его состояние, а не
        /// исчезать, будто цель пропала. У чужого существа при hp=0 обе полоски гаснут.
        /// Полоска жизни НАД телом (lifeBar) — своя у entity, не у рамки: своему игроку она не нужна
        /// вовсе (он видит собственный HP в рамке), поэтому у own её не трогаем совсем.
        /// </summary>
        private void UpdateHpMpLines(EnemyModel entity, bool force)
        {
            bool own = PlayerController.Player != null && entity.key == PlayerController.Player.key;

            if (entity.hp != null)
            {
                if (entity.hp > 0 || own)
                    EnableLine(hpLine);
                else
                    DisableLine(hpLine);

                FillUpdate(hpLine, (float)entity.hp, entity.hpMax, hpText, force);

                if (entity.lifeBar != null && !own)
                {
                    if (entity.hp > 0)
                        EnableLine(entity.lifeBar);
                    else
                        DisableLine(entity.lifeBar);

                    FillUpdate(entity.lifeBar, (float)entity.hp, entity.hpMax, null, force);
                }
            }
            else
                DisableLine(hpLine);

            if (entity.mp != null)
            {
                // Мана завязана на признак жизни тем же правилом, что здоровье: гаснет у мёртвого чужого,
                // остаётся видна у своего игрока (труп-раннер тратит ману так же, как и живой).
                if (entity.mpMax > 0 && ((entity.hp != null && entity.hp > 0) || own))
                    EnableLine(mpLine);
                else
                    DisableLine(mpLine);

                FillUpdate(mpLine, (float)entity.mp, entity.mpMax, mpText, force);
            }
            else
                DisableLine(mpLine);
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
    }
}
