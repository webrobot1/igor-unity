В данной папке структуры ответов - запросов к серверу которые в(из) Json конвертируются.

Для чего именно структуры  если можно все в коде можно все в строку перобразовывать и слать - во первых можн выслать чего лишнего, или прийдет чего лишнее и мы не получим уведомления что неизвестные данные. Плюс ко всему так проще ти нагляднее понять что вообще в пакетах приходит и выылается. Плюс контроль типов данных пересылаемых и оптравляемых далее в модели для работы с ними (опять же если придет что то чего не ждали - выведет ошибку, а мы внесем в клиент исправления)

Правда естьи  минусы - старые клиенты (с устаревшими структурами) не будут работать если на сервере у нас изменился пакет данных (хотя может и не минус, малоли какие механики теперь изменились и добавились)  