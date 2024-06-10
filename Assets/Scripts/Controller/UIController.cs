using System;
using UnityEngine;

namespace Mmogick
{
    /// <summary>
    /// Класс верхнего уровня для управления всеми UI эллементами на экране и передачи в них инфомрацию с сервераб вешается на gameObject на сцене
    /// </summary>
    abstract public class UIController : PlayerController
    {
        [Header("Список всплывающих UI меню")]

        /// <summary>
        /// группа всех меню
        /// </summary>
        [SerializeField]
        private GameObject parentGroup;

        /// <summary>
        /// книга заклинаний
        /// </summary>
        [SerializeField]
        protected CanvasGroup spellGroup;

        /// <summary>
        /// книга заклинаний
        /// </summary>
        [SerializeField]
        protected CanvasGroup settingGroup;        

        protected override void Awake()
        {
            if (spellGroup == null)
            {
                Error("не указан CanvasGroup книги заклинаний");
                return;
            }

            if (settingGroup == null)
            {
                Error("не указана CanvasGroup настроек");
                return;
            }

            if (parentGroup == null)
            {
                Error("не указана CanvasGroup содержащая все остальные CanvasGroup с меню");
                return;
            }
 
            base.Awake();
            CloseAllMenu();   
        }

        protected override void Update()
        {
            base.Update();

            if (Input.GetKeyDown(KeyCode.M))
            {
                OpenClose(spellGroup);
            }            
            
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                OpenClose(settingGroup);
            }
        }

        /// <summary>
        /// публичное что бы любой GameObject мог обратится к методу
        /// </summary>
        public void OpenClose(CanvasGroup canvasGroup)
        {
            // закроем все меню
            CloseAllMenu(canvasGroup);

            // не только скрыть но и позволить кликать по той области что бы ходить персонажем
            canvasGroup.alpha = canvasGroup.alpha>0?0:1;
            canvasGroup.blocksRaycasts = canvasGroup.blocksRaycasts ?false:true;
        }

        protected void CloseAllMenu(CanvasGroup canvasGroup = null)
        {
            if (parentGroup == null)
                Error("не присвоена группа объектов меню объекту UI");

            // закроем все меню
            foreach (Transform child in parentGroup.transform)
            {
                if ((canvasGroup != null && canvasGroup == child.GetComponent<CanvasGroup>()) || (child.GetComponent<CanvasGroup>() == null))
                    continue;

                child.GetComponent<CanvasGroup>().alpha = 0;
                child.GetComponent<CanvasGroup>().blocksRaycasts = false;
            }
        }     

#if !UNITY_EDITOR && (UNITY_WEBGL || UNITY_ANDROID || UNITY_IOS)
		    // повторная загрузка всего пира по новой при переключении между вкладками браузера
		    // если load уже идет то метод не будет отправлен повторно пока не придет ответ на текущий load (актуально в webgl)
		    // TODO придумать как отказаться от этого
		    private void Load()
		    {
                if (player != null)
                {
			        LoadResponse response = new LoadResponse();
			        Send(response);
                }
		    }
#endif

#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
		    public void OnApplicationPause(bool pause)
		    {
			    Debug.Log("Пауза " + pause);
			    Load();
		    }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
		    public void OnApplicationFocus(bool focus)
		    {
                Load();
		    }
#endif
    }
}