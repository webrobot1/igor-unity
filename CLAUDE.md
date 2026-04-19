# Проект (Unity 6000.4.0f1)

## Диагностика игровых объектов (enemy, HP-полоска и т.п.)

Игровые объекты (`enemy`, карта, HP-полоски, Spriter-анимации) существуют только после входа в учётку. Поэтому перед любой диагностикой runtime-поведения:

1. Убедиться, что Unity Editor в playmode — `editor-application-get-state` → `IsPlaying == true`. Если нет, запустить через `editor-application-set-state { isPlaying: true }`.
2. Активная сцена на старте — `Assets/Scenes/RegisterScene.unity`. Там форма входа: `UI/login`, `UI/password`, `UI/server` и две кнопки `UI/Button` (одна из них «Войти»).
3. Нажать «Войти» (UI `Button.onClick.Invoke()` через `script-execute` или `reflection-method-call`). После успешного логина автоматически грузится игровая сцена с enemy.
4. Только после этого выполнять `gameobject-find`, `scene-get-data`, `screenshot-game-view` и пр. по игровым объектам — в `RegisterScene` их нет.


код сервера лежит тут Z:\var\www\html\game  (там свои скилы clude есть)
Errro() - что то типа безопсного exception , что в следдующем кадре отсоединит игрока и выведет ошибку в Ui (что бы не крашить программу, но надо retur делать что бы обратно вернулся поток программы в цикл fixedUpdate  )

Здесь есть AnimalModel, EnemyModel PlayerModel и ObjectModel и префабы на каждую только потому что на сервере сделаны такие kind
И в коде прописаны реакции на определенные event.name, event.Group.name, component.name и entity.action (анимации) в контроллерах и Моделях
Так же в игре прописан код на ряд event.code или component.code когда игроку приходят кастомного вида пакеты (вне world пакетов, например доступные настроки игры или книга заклинаний)

Для другой игры или в прцоессе разработки может состав меняться - следовательно надо менять клиент


## Админка (dev-креды) , но для работы сокнтентом и серерами есть mcp сервер mmogick-websocket (код его в коде сервера)
- URL: http://localhost/admin/
- Login: `admin@my-fantasy.ru`
- Пароль: `123456`
- Для автоматической проверки страниц использовать MCP Playwright.

Есть mcp unity для самостоятельной проверки клиента (пакеты приходящие логируются , на сервере тоже есть лог)
Все настройки проекта Unity что делаются должны быть описаны в readme (какие и зачем)