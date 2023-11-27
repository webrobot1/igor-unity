using UnityEngine;
using UnityEngine.UI;

namespace MyFantasy
{
    public class SigninController : BaseController
    {
        [SerializeField]
        protected Text loginField;

        [SerializeField]
        protected InputField passwordField;        
        
        [SerializeField]
        protected Text gameIdField;      // здесь должен быть указан id ВАШЕГО проекта в личном кабинете http://my-fantasy.ru/  раздела Игры

        protected virtual void Start()
        {
           if (loginField == null)
               Error("не присвоен loginField для ввода логина");

            if (passwordField == null)
                Error("не присвоен passwordField дляв вода пароля");     
            
            if (gameIdField == null)
                Error("не присвоен gameIdField для индентификации в какую ИД игры сервеиса http://my-fantasy.ru/ у разработкчика нужно играть");
        }

        public void Register()
        {
            login = this.loginField.text;
            password = this.passwordField.text;
            game_id = this.gameIdField.text;
        
            StartCoroutine(HttpRequest("register"));
        }

        public void Auth()
        {
            login = this.loginField.text;
            password = this.passwordField.text;
            game_id = this.gameIdField.text;

            StartCoroutine(HttpRequest("auth"));     
        }
    }
}
