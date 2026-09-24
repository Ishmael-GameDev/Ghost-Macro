# Ghost Macro

A Hollow Knight mod that records your run and replays it as a ghost: hitbox trail, real
sprite frames, nail slashes, spells and particle effects. Replays are saved to a compact
`.ghost` file with a built-in replay manager.

Мод для Hollow Knight, который записывает прохождение и проигрывает его призраком:
след хитбоксов, настоящие спрайты, удары гвоздём, заклинания и эффекты частиц.
Записи сохраняются в компактный файл `.ghost`, в моде есть менеджер реплеев.

English version below / Русская версия ниже.

<!--LANG:EN-->
## Ghost Macro (English)

### Install

1. Install the Hollow Knight Modding API and Satchel.
2. Copy `Ghost Macro.dll` into `Hollow Knight_Data/Managed/Mods/Ghost Macro/`.
3. Settings are in Options -> Mods -> Ghost Macro.

### Dependencies

Required:

- Hollow Knight Modding API
- Satchel - used for the mod settings screen

Optional, supported if installed:

- Custom Knight - ghosts are drawn with your current skin
- Benchwarp, DebugMod, QuickSaveStates - Auto Backup saves the trail before a warp or a
  savestate load

This help text is built into the mod, so there is nothing else to copy.

### Quick start

1. Press `J` to start recording, `J` again to stop.
2. Press `P` to play the recording back.
3. Press `M` to save it to a file.
4. Press `O` to open the replay manager.

### Default keys

| Key | Action |
| --- | --- |
| `J` | Start / stop recording |
| `K` | Stop recording (only in Separate record mode) |
| `L` | Clear the trail of the current room |
| `N` | Next trail color |
| `P` | Start playback, pause, resume |
| `M` | Save the trail to a file |
| `O` | Open / close the replay manager |
| `,` | Seek back (only while paused) |
| `.` | Seek forward (only while paused) |

All keys can be rebound in Options -> Mods -> Ghost Macro -> Controls.

### Playback, pause and seeking

Press `P` to start playback and press it again to pause. Seeking only works while paused.
Holding a seek key scrubs the trail for as long as you hold it; a tap shorter than 0.1 s
steps exactly one recorded frame. The status plate shows the playback progress in percent.

In Delayed scene playback mode a short press of `P` pauses and resumes, and holding `P`
for 0.8 seconds turns the delayed playback off.

### Status plate

A small plate shows what the mod is doing right now:

- **RECORDING** - recording is on
- **PLAYING 42%** - playback with progress
- **PAUSED 42%** - paused, seeking is available
- **PLAYBACK: NEXT ROOM** - delayed playback is waiting for you to enter a room
- **UNSAVED TRAIL** - there is a recorded trail that is not saved to a file
- **TRAIL SAVED** / **TRAIL EMPTY** - nothing is pending

### Settings

**Recording**

- **Record Mode** - `Toggle` uses one key for start and stop, `Separate` uses two keys.
- **Particle Effects** - `Light` records cheap particles (dash, shadow dash, wing feathers),
  `All` also records heavy spell particles (spell orbs, scream dust, fireball sparks),
  `Off` records no particles.
- **Record Rate** - trail frames per second: `60`, `120`, `240` or `Full` (every game frame).
  Lower values mean smaller files and less load; 120 looks the same as Full in practice.
- **Save Format** - `Ghost` is the compact binary format, `JSON` is the old readable format.
  Loading understands both regardless of this setting.
- **Auto Backup** - saves an unsaved trail as `..._recovered_backup.ghost` when you exit to
  the menu, warp with Benchwarp or load a savestate (DebugMod, QuickSaveStates).
- **Auto Hide** - hides the trail while recording.

**Display**

- **Trail Visibility** - show or hide the trail.
- **Status Indicator** - which corner the status plate uses, or `Off`.
- **Trail Render Mode** - `Hitboxes` draws the recorded hitbox outline, `Character Frames`
  draws the hero's real sprite frames.
- **Colorize Character Frames** - tint sprite frames with the trail color, or keep their
  natural colors.
- **Custom Knight Skins** - draw ghosts with your current Custom Knight skin, or with the
  original game sprites.

**Hitbox Lines** (only used in Hitboxes mode)

- **Trail Line Smoothing** - feather the edges of the outline.
- **Trail Line Thickness** - `Very Thin`, `Thin`, `Normal`, `Thick`.

**Colors**

- **Active Colors** - how many palette colors are cycled (1-6).
- **Start Color** - the first color of the cycle.

**Playback**

- **Playback Display** - `Trail` keeps every played frame on screen, `Current Only` shows
  just the frame that is playing now.
- **Scene Playback Mode** - `Classic` plays on key press, `Delayed` starts playback when you
  enter a room, timed from the moment you entered it.
- **Input Overlay** - show the ghost's recorded button presses during playback:
  `Off`, `Bottom Left`, `Bottom Center`, `Bottom Right`.
- **Camera Follow Ghost** - during playback the camera follows the ghost instead of the knight.
- **Camera Smoothing** - how softly the camera catches up: `Off` (locked to the ghost) to `0.8s`.

### Replay manager (`O`)

The list shows every `.ghost` and `.json` file, newest first. While the manager is open the
knight does not move and the mod hotkeys are disabled, so typing is safe. `Esc` cancels
renaming, goes back from Rooms, then closes the manager.

The first row is **Unsaved recording** - the trail that is still only in memory. It shows up
only while the recording is not saved and disappears once you save it; it comes back as soon
as you record something new. You do not have to save it first: **Save** writes it to a file,
**Rooms** opens the room editor for it, and there its Save button writes a new file with
whatever you trimmed. The row has its own name field next to **Save**, so the recording is
saved under the name you type; the same field is in the room editor and in the workshop.
An existing file is never overwritten - a number is added to the name instead.

Per replay:

- **Load** - load the replay and close the manager.
- **Rooms** - open the room editor (see below).
- **Copy** - copy the file itself to the clipboard, so you can paste it into Discord,
  Telegram or a folder with Ctrl+V. On Linux and macOS the file path is copied instead.
- **Rename** - the row turns into a name field. Enter or **OK** confirms, Esc cancels.
- **Show** - open the folder with the file selected.
- **Convert** - for `.json` files: convert to `.ghost`.
- **Delete** - red button, works only if you hold it for one second. The file is copied into
  the `Backups` subfolder before it is removed.

The panel on the right shows the metadata: note, recording stats (duration, frames, average
game FPS, route, distance), input counts, player state (masks, vessels, nail, notches,
spells, abilities, charms) and a per-room breakdown. Charm names are always in English.

**Note** - each replay can have a text note. Saving a note only rewrites the file header,
the trail itself is untouched.

### Room editor (Rooms)

Every visit to a room is a separate card with a thumbnail of the trail. The thumbnail can
show the trail only or the trail plus effect positions.

- Drag the two slider handles to choose the part of the room you want to keep. The mouse
  wheel moves the handle nearest to the cursor by 1%, or 5% with Shift.
- **Trim** applies the range, **Cancel** restores the whole room. Dragging both handles
  together removes the room.
- **Save (backup original)** copies the original file into the `Backups` subfolder and then
  rewrites the file.

### Workshop

**Workshop** builds a new replay out of rooms taken from different files (and from the
current recording).

1. Pick a source replay at the top left.
2. Press **Add to project** on the rooms you want.
3. Order them with **Up** / **Down**, drop one with **Remove**.
4. **Build & Save** writes a new `workshop_...ghost` file.

One scene can be used only once per project: two visits of the same room would be drawn at
the same time and overlap each other, so a second one cannot be added. When building, the
rooms are placed one after another on the timeline with a small gap, and the in-room timing
is kept, so Delayed scene playback still works.

### Files

Replays are stored next to the save files:

- Windows: `%USERPROFILE%\AppData\LocalLow\Team Cherry\Hollow Knight\GhostMacro`
- macOS: `~/Library/Application Support/unity.Team Cherry.Hollow Knight/GhostMacro`
- Linux: `~/.config/unity3d/Team Cherry/Hollow Knight/GhostMacro`

`Open Folder` in the manager opens exactly that folder.

### The `.ghost` format

The old JSON format took about 40 MB per minute of recording at 300 FPS. The same data in
`.ghost` is roughly a hundred times smaller. It packs the data in layers: a string table, a
sprite key table, quantization, delta coding, ZigZag varints, a shortened matrix and Deflate
on top. Metadata sits in the header uncompressed, so the manager can list files without
unpacking the trails.

Loading is also cheaper: the file is decoded straight into the mod's runtime structures and
each unique sprite is resolved once per file instead of once per frame.

### Notes and limits

- Particle effects are stored as a compressed particle cloud. Heavy effects are recorded at a
  lower rate and their late, faded particles are dropped: they cost the most to draw and are
  barely visible.
- Room thumbnails are a schematic path plot, not a render of the real sprites.
- Recording only captures trail frames, not the game state. A replay is a visual ghost, not a
  TAS playback.

<!--LANG:RU-->
## Ghost Macro (Русский)

### Установка

1. Установите Hollow Knight Modding API и Satchel.
2. Скопируйте `Ghost Macro.dll` в папку `Hollow Knight_Data/Managed/Mods/Ghost Macro/`.
3. Настройки находятся в Options -> Mods -> Ghost Macro.

### Зависимости

Обязательные:

- Hollow Knight Modding API
- Satchel - на нём сделан экран настроек мода

Необязательные, поддерживаются при наличии:

- Custom Knight - призраки рисуются текущим скином
- Benchwarp, DebugMod, QuickSaveStates - Auto Backup сохраняет след перед варпом или
  загрузкой сейвстейта

Эта справка вшита в мод, ничего дополнительно копировать не нужно.

### Быстрый старт

1. `J` - начать запись, `J` ещё раз - остановить.
2. `P` - проиграть запись.
3. `M` - сохранить её в файл.
4. `O` - открыть менеджер реплеев.

### Клавиши по умолчанию

| Клавиша | Действие |
| --- | --- |
| `J` | Начать / остановить запись |
| `K` | Остановить запись (только в режиме Separate) |
| `L` | Очистить след текущей комнаты |
| `N` | Следующий цвет следа |
| `P` | Запуск проигрывания, пауза, продолжение |
| `M` | Сохранить след в файл |
| `O` | Открыть / закрыть менеджер реплеев |
| `,` | Перемотка назад (только на паузе) |
| `.` | Перемотка вперёд (только на паузе) |

Все клавиши переназначаются в Options -> Mods -> Ghost Macro -> Controls.

### Проигрывание, пауза и перемотка

`P` запускает проигрывание, повторное нажатие ставит паузу. Перемотка работает только на
паузе. Удержание клавиши перематывает след на время удержания, а нажатие короче 0.1 с
сдвигает ровно на один записанный кадр. Плашка показывает процент проигрыша комнаты.

В режиме отложенного проигрывания короткое нажатие `P` ставит паузу и снимает её, а
удержание `P` в течение 0.8 секунды выключает отложенный реплей.

### Плашка статуса

Небольшая плашка показывает, что мод делает прямо сейчас:

- **RECORDING** - идёт запись
- **PLAYING 42%** - проигрывание с прогрессом
- **PAUSED 42%** - пауза, доступна перемотка
- **PLAYBACK: NEXT ROOM** - отложенный реплей ждёт входа в комнату
- **UNSAVED TRAIL** - есть записанный след, не сохранённый в файл
- **TRAIL SAVED** / **TRAIL EMPTY** - несохранённого следа нет

### Настройки

**Recording (запись)**

- **Record Mode** - `Toggle`: одна клавиша на старт и стоп, `Separate`: две клавиши.
- **Particle Effects** - `Light`: только лёгкие частицы (деш, теневой деш, перья крыльев),
  `All`: плюс тяжёлые частицы заклинаний (сферы пике и крика, искры огнешара),
  `Off`: частицы не записываются.
- **Record Rate** - кадров следа в секунду: `60`, `120`, `240` или `Full` (каждый кадр игры).
  Меньше значение - меньше файл и нагрузка; на глаз 120 не отличается от Full.
- **Save Format** - `Ghost`: компактный бинарный формат, `JSON`: старый читаемый формат.
  Загрузка понимает оба формата независимо от этой настройки.
- **Auto Backup** - сохраняет несохранённый след как `..._recovered_backup.ghost` при выходе
  в меню, варпе Benchwarp и загрузке сейвстейта (DebugMod, QuickSaveStates).
- **Auto Hide** - скрывает след во время записи.

**Display (отображение)**

- **Trail Visibility** - показывать след или нет.
- **Status Indicator** - угол экрана для плашки статуса или `Off`.
- **Trail Render Mode** - `Hitboxes`: рисуется контур хитбоксов, `Character Frames`:
  рисуются настоящие спрайты рыцаря.
- **Colorize Character Frames** - красить спрайты в цвет следа или оставить их родные цвета.
- **Custom Knight Skins** - рисовать призраков текущим скином Custom Knight или
  оригинальными спрайтами игры.

**Hitbox Lines (линии хитбоксов, только в режиме Hitboxes)**

- **Trail Line Smoothing** - сглаживание краёв контура.
- **Trail Line Thickness** - `Very Thin`, `Thin`, `Normal`, `Thick`.

**Colors (цвета)**

- **Active Colors** - сколько цветов палитры используется (1-6).
- **Start Color** - первый цвет цикла.

**Playback (проигрывание)**

- **Playback Display** - `Trail`: все проигранные кадры остаются на экране,
  `Current Only`: виден только текущий кадр.
- **Scene Playback Mode** - `Classic`: проигрывание по нажатию клавиши,
  `Delayed`: начинается при входе в комнату, отсчёт идёт от момента входа.
- **Input Overlay** - показывать записанные нажатия призрака во время проигрывания:
  `Off`, `Bottom Left`, `Bottom Center`, `Bottom Right`.
- **Camera Follow Ghost** - во время проигрывания камера следует за призраком, а не за рыцарем.
- **Camera Smoothing** - мягкость доводки камеры: от `Off` (жёстко на призраке) до `0.8s`.

### Менеджер реплеев (`O`)

В списке все файлы `.ghost` и `.json`, новые сверху. Пока менеджер открыт, рыцарь не
управляется, а горячие клавиши мода отключены, поэтому набирать текст безопасно. `Esc`
отменяет переименование, возвращает из комнат, затем закрывает менеджер.

Первая строка - **Unsaved recording**, то есть след, который пока лежит только в памяти.
Она видна, только пока запись не сохранена: после сохранения строка исчезает и появляется
снова, как только вы запишете что-то новое. Сохранять заранее не нужно: **Save** запишет след
в файл, **Rooms** откроет для него редактор комнат, и там кнопка сохранения создаст новый
файл уже с обрезкой. Рядом с кнопкой **Save** есть поле имени, поэтому запись сохраняется
под тем именем, которое вы введёте; такое же поле есть в редакторе комнат и в мастерской.
Существующий файл не перезаписывается - к имени добавляется номер.

По каждому реплею:

- **Load** - загрузить реплей и закрыть менеджер.
- **Rooms** - открыть редактор комнат (ниже).
- **Copy** - скопировать сам файл в буфер обмена: его можно вставить в Discord, Telegram
  или папку по Ctrl+V. На Linux и macOS копируется путь к файлу.
- **Rename** - строка превращается в поле ввода имени. Enter или **OK** применяет, Esc отменяет.
- **Show** - открыть папку с выделенным файлом.
- **Convert** - для файлов `.json`: конвертировать в `.ghost`.
- **Delete** - красная кнопка, срабатывает только при удержании одну секунду. Перед
  удалением файл копируется в подпапку `Backups`.

Панель справа показывает метаданные: заметку, статистику записи (длительность, кадры,
средний FPS игры, маршрут, дистанцию), счётчики нажатий, состояние игрока (маски, сосуды,
гвоздь, ячейки, заклинания, способности, амулеты) и разбивку по комнатам. Названия амулетов
всегда английские.

**Note** - к каждому реплею можно написать заметку. Её сохранение переписывает только
заголовок файла, сам след не трогается.

### Редактор комнат (Rooms)

Каждое посещение комнаты - отдельная карточка с миниатюрой следа. Миниатюра показывает
только след или след вместе с позициями эффектов.

- Перетаскивайте две ручки ползунка, выбирая часть комнаты, которую нужно оставить. Колесо
  мыши двигает ближайшую к курсору ручку на 1%, с Shift - на 5%.
- **Trim** применяет обрезку, **Cancel** возвращает комнату целиком. Если свести ручки
  вместе, комната будет удалена.
- **Save (backup original)** копирует оригинал в подпапку `Backups` и перезаписывает файл.

### Мастерская (Workshop)

**Workshop** собирает новый реплей из комнат разных файлов (и из текущей записи).

1. Выберите файл-источник слева сверху.
2. Нажмите **Add to project** на нужных комнатах.
3. Поменяйте порядок кнопками **Up** / **Down**, лишнее уберите кнопкой **Remove**.
4. **Build & Save** запишет новый файл `workshop_...ghost`.

Одна сцена может войти в проект только один раз: два посещения одной комнаты рисовались бы
в ней одновременно и накладывались друг на друга, поэтому второе добавить нельзя. При
сборке комнаты выстраиваются на таймлайне одна за другой с небольшим зазором, а время
внутри комнаты сохраняется, так что отложенное проигрывание работает как раньше.

### Где лежат файлы

Реплеи хранятся рядом с сохранениями игры:

- Windows: `%USERPROFILE%\AppData\LocalLow\Team Cherry\Hollow Knight\GhostMacro`
- macOS: `~/Library/Application Support/unity.Team Cherry.Hollow Knight/GhostMacro`
- Linux: `~/.config/unity3d/Team Cherry/Hollow Knight/GhostMacro`

Кнопка `Open Folder` в менеджере открывает именно эту папку.

### Формат `.ghost`

Старый формат JSON занимал около 40 МБ на минуту записи при 300 FPS. Те же данные в
`.ghost` примерно в сто раз меньше. Сжатие идёт слоями: таблица строк, таблица ключей
спрайтов, квантование, дельта-кодирование, ZigZag-varint, укороченная матрица и Deflate
поверх всего. Метаданные лежат в заголовке без сжатия, поэтому менеджер строит список
файлов, не распаковывая сами следы.

Загрузка тоже легче: файл декодируется сразу в рабочие структуры мода, а каждый уникальный
спрайт резолвится один раз на файл, а не на каждый кадр.

### Особенности и ограничения

- Частицы хранятся сжатым облаком. Тяжёлые эффекты пишутся реже, а их поздние выцветшие
  частицы отбрасываются: рисовать их дороже всего, а видно их почти не было.
- Миниатюры комнат - это схема пути, а не отрисовка настоящих спрайтов.
- Запись сохраняет только кадры следа, а не состояние игры. Реплей - это визуальный призрак,
  а не воспроизведение ввода.
