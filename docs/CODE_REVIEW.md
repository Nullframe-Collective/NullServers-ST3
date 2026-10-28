# Ревью кода NullServers (28.10.2026)

Бэкап исходников до правок: `research/backups/before-claude-review-20261028/`.
Формат сетевых пакетов (JSON-поля, ID broadcast'ов, версии протокола) не менялся: клиенты с новой и старой сборкой совместимы.

## Что исправлено

### Производительность (горячие пути)
| Где | Было | Стало |
|---|---|---|
| `FrameworkCompatibility.Ready` | Вызывается из десятков Harmony-префиксов каждый кадр (`FPScontroller.Update/FixedUpdate`, `FPSMouseLook`, `WeaponScript.Update`, `cInput.GetButtonDown`, урон). Каждый вызов: новый `FrameworkManifest`, ~20 LINQ-аллокаций, 8+ вызовов `PropertyInfo.GetValue` | Считается один раз за кадр, кэш сбрасывается при изменении состояния |
| `RelaySession.Content`, `Frameworks.Installed/Selected` | Пересобирались при каждом обращении | Кэшируются, пересобираются только при смене манифеста или выбора аддонов |
| `SandboxWorld.Avatar(id)` | `FindObjectsOfType<PlayerNetworkController>()` на каждый вызов: на каждого пира 20 раз/с, на каждый хват в каждом физическом тике, в `LateUpdate` | Один скан за кадр, словарь по `OwnerId` |
| `SandboxWorld.ConsoleEnabled` | `FindObjectsOfType<Volume>()` на каждый вызов, при строительстве 2–3 раза за кадр, в `Hands.FixedUpdate` на каждый хват | Общий список `Volumes`, один скан за кадр (им же пользуется `SandboxMenu.Update`) |
| `SandboxAssets` | В **любой** NullFrame-комнате каждые 15 с: `Resources.FindObjectsOfTypeAll<GameObject>()` + обход всех `MeshFilter` + SHA-256; иконки рендерились заранее, по одной за кадр (`Camera.Render` + `ReadPixels`, то есть синхронизация с GPU) | Полный скан только при открытии каталога или при неизвестном ID пропа, не чаще раза в 15 с; иконки рендерятся только для показанных плиток |
| `RagdollSync` (клиент) | Непривязанное тело пыталось привязаться каждый кадр, каждая неудача = `Resources.FindObjectsOfTypeAll<GameObject>()` | Повтор раз в секунду |
| `LivingRagdolls` | `Bind` каждый кадр, внутри `FindObjectsOfType<NetworkObject>()`; `FindObjectsOfType<FPScontroller>()` каждый кадр в `LateUpdate` | Повтор 5 раз в секунду, NPC ищутся через таблицу FishNet `Spawned` (O(1)), контроллер берётся из кэша |
| `SandboxBuilding` | Каждый кадр текстура переприсваивалась всем материалам всех текстурированных пропов (до 8192); наведение и выбор = `IsChildOf` по всем объектам; `GetComponentsInChildren` у призрака каждый кадр | Текстура применяется только при смене; поиск пропа подъёмом по родителям до `Build_<id>`; рендереры призрака закэшированы |
| `PhysicsReplication`, снапшоты | Каждый пакет сериализовался в JSON дважды: первый раз только ради размера | Сериализуется один раз |
| `GUI.Label` / `Text.text` (глобальные патчи) | Для **каждой** надписи в игре: доступ к IL2CPP-объекту комнаты, конкатенация строк, 2 разбора JSON на кадр | Сначала дешёвая проверка `Contains("Room ID: ")`, разбор имени и цвета комнаты мемоизирован |
| Список комнат | `RoomName.ReadDisplay` и `RoomLabels.FormatGameMode` разбирали JSON для каждой строки на каждом событии OnGUI | Мемоизированы |
| `BeforeLook`, `BeforeWeapon`, `BeforeMovement` | `GetComponentInParent` вызывался до дешёвых проверок | Дешёвые проверки идут первыми |

### Баги
- **`PlayerProfiles.RememberPlayers`**: условие требовало, чтобы игрок был в `_serverProfiles`, а эта таблица заполняется только у хоста. Поэтому у клиентов кэш профилей не обновлялся, и после смены ника или цвета все, кроме хоста, видели старое значение до выхода из комнаты. Исправлено, заодно убран `FindObjectsOfType<PlayerInfo>()`.
- **`??=` на Unity-объектах** (`BuildingProps._material`, `LivingRagdolls.actor.Source`): уничтоженный объект в C# не равен `null`, поэтому замена не происходила. Заменено на `if (!obj)`. Кэшированные меш и материал пропов помечены `DontUnloadUnusedAsset`.
- **Необработанный JSON в Harmony-патчах** (`RelaySession.UpdateRoom/BeforeRoomSettings`, `ServerControl.AfterRoomUpdated`, `MapRooms`): `JsonNode.Parse(...).AsObject()` и `GetValue<string>()` бросали исключение прямо в сетевой код FishNet, если данные комнаты кривые. Теперь всё идёт через `Room/RoomData.cs`.
- `SandboxBuilding.Remove` вызывал `Hands.Removed(id)` дважды.
- `ReadJson` при ошибке писал в лог «Invalid player profile packet» для любого типа пакета.

### Безопасность
- **SSRF / доступ к локальной сети через скины.** URL скинов (и текстур пропов в песочнице, а их может задать любой игрок) скачивались через `UnityWebRequest` с редиректами и без проверки адреса. Любой игрок мог заставить клиенты всех остальных отправить GET-запрос на `192.168.0.1`, `localhost:порт` и т.п. Для неба уже был защищённый `PublicImage` (DNS pinning, отсев приватных IP, проверка заголовка PNG/JPEG до декодирования). Теперь скины тоже идут через него, а редиректы проверяются на каждом шаге (максимум 3, без перехода HTTPS→HTTP).

### Упрощения
- `SandboxMessage.TryParse`: выражение на ~60 строк со смесью `&&`/`||` без скобок разбито на `ValidHeader / ValidPayload / ValidRagdoll / HasRequiredPayload` (`switch`). Семантика та же, все 1257 проверок проходят.
- Две одинаковые реализации token bucket (`TrafficBudget` и `RpcBudget.Bucket`) объединены в одну.
- `ServerControl`: повторные `if (!CanManage)` сразу после раннего `return` убраны, общие `KillFeed()` и `RequestUpdate(changes, scene)` вместо копипасты.
- Ручной поиск индекса среди соседей заменён на `Transform.GetSiblingIndex()`.
- Логгер стал обязательным параметром (раньше `logger = null` и вперемешку `_logger?.` / `_logger.`).
- `.csproj`: 104 ручных `<Reference>` заменены двумя wildcard-строками с `Private=false` (в `bin` больше не копируются ~100 DLL игры), `COPY` заменён на MSBuild-задачу `Copy`, убран ненужный `AllowUnsafeBlocks`.

## Что осталось (не правил: нужно ваше решение или тест на двух клиентах)

1. **Утечка IP.** URL неба задаёт хост, текстуры пропов задаёт любой игрок, и все клиенты автоматически скачивают картинку. Владелец URL видит IP каждого игрока в комнате. Варианты: белый список хостингов (imgur, Discord CDN и т.п.) или настройка «не загружать чужие картинки».
2. **Нет иерархии прав.** Доверенный админ может кикнуть или забанить другого админа и снять любой бан, поставленный хостом.
3. **Блэклист — единая точка отказа.** Пока список с GitHub не загрузится (`Blacklist.Ready == false`), войти нельзя ни в одну NullFrame-комнату. Если GitHub недоступен, не играет никто.
4. **`BanList`**: если файл повреждён, `LoadError` навсегда запрещает сохранение, и хост не может никого забанить, пока сам не удалит файл.
5. **`WorldGeometry.Capture`** пересканирует всю сцену (по 5 `GetComponentInParent` и 4 `GetComponentsInChildren` на узел) каждый раз, когда меняется число корневых объектов. В песочнице это может происходить часто.
6. **`SkyboxImage`**: конвертация equirect→cubemap (6×256×256 с `Atan2/Asin`) идёт на главном потоке; её можно вынести в `Task`, это чистая математика над `Color32[]`.
7. **`PlayerProfiles.ReadMessages`** читает ID пакета до 6 раз подряд (по разу на каждый тип). Лучше прочитать один раз и сделать `switch`.
8. **Протокол.** Позы физики идут как Base64 внутри JSON внутри broadcast'а. Бинарный формат был бы в 2–3 раза компактнее, но это ломает совместимость, поэтому только с новой версией протокола.
9. **Гигиена репозитория.** Нет git, вместо него 236 МБ копий проекта в `research/backups` (вместе с Unity `Library/PackageCache`). Рекомендую `git init` и `.gitignore` для `research/backups`, `dist/*.zip`, `bin`, `obj`.
10. **Документация.** `FRAMEWORKS.md` описывает версии 0.0.25–0.0.29, а в `MelonInfo` стоит 0.0.37, в `AssemblyVersion` 1.0.0.0.
