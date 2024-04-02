using System;
using UnityEngine;

namespace MyFantasy
{
    /// <summary>
    /// Класс верхнего уровня для управления всеми UI эллементами на экране и передачи в них инфомрацию с сервераб вешается на gameObject на сцене
    /// </summary>
    public class UIController : SettingsController
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
        private CanvasGroup spellGroup;

        /// <summary>
        /// книга заклинаний
        /// </summary>
        [SerializeField]
        private CanvasGroup settingGroup;        

        /// <summary>
        /// если мы стреляем и продолжаем идти заблокируем поворот (он без запроса к серверу делется) в сторону хотьбы (а то спиной стреляем)
        /// </summary>
        private DateTime block_forward = DateTime.Now;

        /// <summary>
        /// Singleton instance of the handscript
        /// </summary>
        private static UIController _instance;

        public static UIController Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<UIController>();
                }

                return _instance;
            }
        }

        protected override void Awake()
        {
            if (spellGroup == null)
                Error("не указан CanvasGroup книги заклинаний");           
            
            if (settingGroup == null)
                Error("не указана CanvasGroup настроек");

            if (parentGroup == null)
                Error("не указана CanvasGroup содержащая все остальные CanvasGroup с меню");

            _instance = this;

            base.Awake();
            CloseAllMenu();   
        }

        protected override GameObject UpdateObject(int map_id, string key, EntityRecive recive, string type)
        {
            // если с сервера пришла анимация заблокируем повороты вокруг себя на какое то время (а то спиной стреляем идя и стреляя)
            if (player != null && key == player.key && recive.action!=null)
            {
                block_forward = DateTime.Now.AddSeconds(0.2f);
            }

            return base.UpdateObject(map_id, key, recive, type);
        }

        protected override void Update()
        {
            base.Update();

            if (Input.GetKeyDown(KeyCode.P))
            {
                OpenClose(spellGroup);
            }            
            
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                OpenClose(settingGroup);
            }
        }

        public void OpenClose(CanvasGroup canvasGroup)
        {
            // закроем все меню
            CloseAllMenu(canvasGroup);

            // не только скрыть но и позволить кликать по той области что бы ходить персонажем
            canvasGroup.alpha = canvasGroup.alpha>0?0:1;
            canvasGroup.blocksRaycasts = canvasGroup.blocksRaycasts ?false:true;
        }

        private void CloseAllMenu(CanvasGroup canvasGroup = null)
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