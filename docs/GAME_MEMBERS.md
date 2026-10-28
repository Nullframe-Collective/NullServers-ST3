# Имена членов игры и как они проверены

Источник: `GameAssembly.dll` + `global-metadata.dat` (Unity 6000.0.82, metadata v31). Разбор сделан своими скриптами из `research/tools/il2cpp/`, без Cpp2IL и сторонних дампов:

- `il2cpp_reader.py` читает таблицы метаданных (типы, методы, поля, строковые литералы). В бинарнике он находит `Il2CppCodeGenModule` (указатели методов по токену) и `Il2CppMetadataRegistration` (смещения полей, таблица типов).
- `disasm.py` дизассемблирует метод через capstone и подписывает вызовы, поля (по смещению) и ссылки на строки и типы (слоты metadata usage: `kind = v >> 29`, `index = (v & 0x1FFFFFFF) >> 1`). Режимы: `ctx Тип Поле` (кто пишет и читает поле), `stores 0xСМЕЩЕНИЕ` (все записи по смещению), `callers Тип Метод` (места вызова).

## Подтверждённые по коду

| Имя в коде | Оригинал | Доказательство |
|---|---|---|
| `RoomMultiplayerMenu.IsInitialized` | `CJNLEDLNGID` | Ставится в 1 в корутине старта комнаты после `SetTeam("Spectators")` и `ResetScore()`; сразу после этого идёт спавн по режиму (`SUR`/`COOP`/`VS`/…) |
| `RoomMultiplayerMenu.IsPaused` | `IOMIAHNBDOG` | Переключается в `Update` по кнопке `"Menu"` вместе с `Cursor.lockState`; сбрасывается в `Unpause()` |
| `PlayerInfo.Kills` / `Deaths` | `ANGFKCCEJKD` / `DDLEOEFHMKL` | Статический компаратор таблицы игроков сортирует по первому по убыванию, по второму по возрастанию, затем по id |
| `PlayerInfo.ResetScore()` | `LEEDMMPNCJB` | Через ServerRpc и SyncVar ставит оба поля счёта (0x108 / 0x110) в 0 |
| `PlayerDamage.TakeDamage(amount, attacker)` | `LAKFCNKPBPM` | Запоминает время удара; на клиенте вызывает локальное применение `KDDDGBFEFOC` и RPC на сервер |
| `FPScontroller.Grounded` | `DBNLEAKGLGC` | `true` в конструкторе, сбрасывается перед `SendMessage("OnExternalVelocity")` (паттерн CharacterMotor.SetVelocity) |
| `SyncCustomization.SkinsDisabled` | `CNNHJCHFEBO` | Читается из `ObscuredPrefs.GetBool("DisableSkins")` |
| `SyncCustomization.FromLegacyReplay` | `IGDAIDNEJBO` | Ставится в 1 парсером старого формата (разделители `` ` `` и `:`) и в 0 парсером нового; оба вызываются только из `TheatreMode` |
| `SyncCustomization.AccentRoot` | `ELPJDJFCCNJ` | Рендереры под этим узлом красятся вторым цветом (поле 0x160), не цветом меха |
| `SyncCustomization.HatId` / `FurColor` / `SkinData` | `CLENBEHPEEL` / `LJALDBMBHHN` / `CLABNNBNKAF` | Заполняются парсерами внешнего вида: `Int32.Parse` → hat, три `Single.Parse` → цвет, строка → скин |
| `LobbyMenu.IsCreatingRoom` | `EJLDOIOJGPC` | Ставится в 1 при создании комнаты (рядом сообщение `" - share it with friends"`) |
| `LobbyMenu.IsModeListOpen` | `EGDMPMDBJIK` | Ставится в 1 и 0 кнопками выпадающего списка; сбрасывается при открытии браузера комнат |
| `NetBootstrap.PendingScene` | `HNHLFANHCFG` | Строка сцены, которую передают в загрузчик |
| `NetBootstrap.RoomSettings` | `<GCBPPKIDDHB>k__BackingField` | Заполняется из `RoomProperties.HFBENCJFGPO` (= `RoomProperties.Parse`), читается `RoomProperties.Current` |
| `MainMenu.ServerPanel` | `OJKJNHPHFFO` | `GameObject`, который `SelectedServer` включает и выключает |

Остальные имена фасада (`Damage`, `Health`, `RagdollPrefab`, `Categories`, `Rooms`, `Servers`, …) следуют из типа члена и единственного способа использования; их типы сверены с interop-сборками.

`LeftArm`/`RightArm` и `LeftLeg`/`RightLeg` — без подтверждения стороны: в коде игры обе руки и обе ноги обрабатываются одинаково, сторона берётся из позы в превью мода.

## Не переименованы (смысл не подтверждён)

`LobbyMenu.BAJDIMCNBDM`, `LobbyMenu.FAENEDGHCPN`, `NetBootstrap.NNLCAJPMCPG`, `<MADNGLFHLMO>k__BackingField`, внутренние поля UI `Volume` (`KDABMADBABK`, `DKOOOHKFCDK`, `FIKDALMIMAB`, `EGNKPPJHIBA`), а также `nameof(...)` целей Harmony-патчей.
