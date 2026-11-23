# VeloxTune

A lightweight, sleek, and high-performance audio player for Windows designed with a fluent dark glassmorphic interface, responsive album art, robust playlist management, and smart background audio controls.

---

## English

### Overview
VeloxTune is an elegant desktop audio player crafted for users who appreciate minimalism, speed, and clean design. Built with WPF on modern .NET, it delivers an instant, responsive native Windows experience without the overhead of web-based electron wrappers.

### Key Features

- **Fluent Dark Design**: Borderless window with rounded corners, subtle radial glow, and smooth animations.
- **Single Instance Architecture**: Double-clicking audio files in Windows Explorer immediately plays them in the existing window without opening duplicate instances.
- **Interactive Slide-over Drawer**:
  - **Playlists & Playback Queue**: Organized in dedicated tabs matching the top bar.
  - **Drag-and-Drop Reordering**: Rearrange tracks inside the queue or playlists with live insertion indicators.
  - **Track Row Actions**: Quick button to duplicate or add any song to the queue, and remove button to delete.
  - **Saved Playlists**: Create, reorder, and click-to-rename playlists directly inside the player.
  - **M3U8 Support**: Export and import playlists using standard .m3u8 and .m3u files.
- **Adaptive Artwork Engine**:
  - Automatically reads embedded ID3v2, Vorbis, FLAC, and MP4 cover art.
  - Uses high-quality bicubic filtering for photo covers and crisp nearest-neighbor rendering for pixel art and retro graphics.
- **Precision Seeking & Volume**:
  - **Timeline Seekbar**: 16px magnetic snap threshold at the start (00:00) and end; hover for 500ms to preview the exact timestamp before jumping.
  - **Volume Slider**: 10px snap zones for instant mute (0%) and maximum volume (100%); live percentage tooltip on hover and drag.
- **Smart Headphone & Media Controls**:
  - Headphone play/pause button support system-wide.
  - Rapid double-tap volume skipping to switch tracks while preserving your system volume level.
- **Bilingual Interface**: Automatically detects system locale (Russian and English).
- **In-App Notifications**: Clean toasts and modal confirmation dialogs instead of OS message boxes.

### Supported Formats
- **Audio Files**: .mp3, .flac, .wav, .m4a, .aac, .mp4 (audio track), .ogg, .opus
- **Playlists**: .m3u8, .m3u

---

## Русский

### Описание
VeloxTune — это быстрый, компактный и стильный аудиоплеер для Windows, разработанный для ценителей минимализма, высокой производительности и качественного интерфейса. Написан на WPF и современном .NET, благодаря чему потребляет минимум ресурсов и запускается мгновенно без громоздких веб-оболочек (Electron).

### Основные возможности

- **Современный тёмный дизайн**: Стильное окно с закруглёнными углами, мягким неоновым свечением и плавными переходами.
- **Один экземпляр программы (Single Instance)**: При открытии аудиофайла через Проводник плеер мгновенно воспроизводит его в уже открытом окне, не создавая дублирующих копий.
- **Интерактивная боковая панель**:
  - **Вкладки «Плейлисты» и «Очередь»**: Удобное переключение в шапке панели в соответствии с кнопками главного меню.
  - **Перетаскивание треков (Drag-and-Drop)**: Удобное изменение порядка треков в очереди и плейлистах с визуальной индикаторной полосой.
  - **Действия в строках треков**: Кнопка для быстрого добавления файла в очередь воспроизведения и кнопка для удаления из списка.
  - **Локальные плейлисты**: Создание, сортировка и переименование плейлистов кликом по названию прямо в плеере.
  - **Экспорт и импорт M3U8**: Сохранение и загрузка плейлистов в стандартных форматах .m3u8 и .m3u.
- **Адаптивные обложки альбомов**:
  - Автоматическое извлечение встроенных обложек (ID3v2, Vorbis Comments, FLAC, MP4).
  - Высококачественная бикубическая фильтрация для фотографий альбомов и пиксельная чёткость (Nearest-Neighbor) для ретро-обложек и пиксель-арта.
- **Высокоточный таймлайн и громкость**:
  - **Ползунок времени**: Зона прилипания 16 пикселей у начала (00:00) и конца; всплывающая подсказка с точным временем при наведении курсора.
  - **Ползунок звука**: Зоны мгновенного прилипания 10 пикселей для выключения звука (0%) и максимальной громкости (100%); динамическая индикация процентов при наведении и перетягивании.
- **Умное управление мультимедиа**:
  - Поддержка аппаратных кнопок воспроизведения/паузы на гарнитурах и наушниках.
  - Быстрое двойное нажатие громкости для переключения треков с сохранением системного уровня звука.
- **Двуязычный интерфейс**: Автоматическое определение языка системы (русский и английский).
- **Встроенные уведомления**: Элегантные toast-оповещения и затемнённые модальные диалоги вместо стандартных белых окон Windows MessageBox.

### Поддерживаемые форматы
- **Аудиофайлы**: .mp3, .flac, .wav, .m4a, .aac, .mp4 (аудиопоток), .ogg, .opus
- **Файлы плейлистов**: .m3u8, .m3u

---

### Лицензия / License
MIT License. Free for personal and commercial use.
