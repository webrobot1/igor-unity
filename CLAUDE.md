# Проект (Unity 6000.4.0f1)

## Диагностика игровых объектов (enemy, HP-полоска и т.п.)

Игровые объекты (`enemy`, карта, HP-полоски, Spriter-анимации) существуют только после входа в учётку. Поэтому перед любой диагностикой runtime-поведения:

1. Убедиться, что Unity Editor в playmode — `editor-application-get-state` → `IsPlaying == true`. Если нет, запустить через `editor-application-set-state { isPlaying: true }`.
2. Активная сцена на старте — `Assets/Scenes/RegisterScene.unity`. Там форма входа: `UI/login`, `UI/password`, `UI/server` и две кнопки `UI/Button` (одна из них «Войти»).
3. Нажать «Войти» (UI `Button.onClick.Invoke()` через `script-execute` или `reflection-method-call`). После успешного логина автоматически грузится игровая сцена с enemy.
4. Только после этого выполнять `gameobject-find`, `scene-get-data`, `screenshot-game-view` и пр. по игровым объектам — в `RegisterScene` их нет.
