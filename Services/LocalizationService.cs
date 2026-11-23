using System;
using System.Globalization;

namespace ModernPlayer.Services
{
    public static class LocalizationService
    {
        public static bool IsRussian { get; } =
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase);

        public static string T(string ru, string en) => IsRussian ? ru : en;

        // Common Strings
        public static string AppTitle => "VeloxTune";
        public static string NoFile => T("Нет файла", "No file");
        public static string OpenFileHint => T("Откройте файл или перетащите сюда", "Open a file or drag & drop here");
        public static string UnknownArtist => T("Без исполнителя", "Unknown Artist");

        // Tooltips & Buttons
        public static string Minimize => T("Свернуть", "Minimize");
        public static string Maximize => T("Развернуть", "Maximize");
        public static string Restore => T("Восстановить", "Restore");
        public static string Close => T("Закрыть", "Close");
        public static string ClosePanel => T("Закрыть панель (Esc)", "Close panel (Esc)");

        public static string Playlists => T("Плейлисты", "Playlists");
        public static string PlaybackQueue => T("Очередь воспроизведения", "Playback queue");

        public static string ShuffleOn => T("Случайный порядок (Вкл)", "Shuffle (On)");
        public static string ShuffleOff => T("Случайный порядок (Выкл)", "Shuffle (Off)");

        public static string RepeatOff => T("Повтор: Выкл", "Repeat: Off");
        public static string RepeatAll => T("Повтор: Все файлы (Вкл)", "Repeat: All files (On)");
        public static string RepeatOne => T("Повтор: Один файл (1)", "Repeat: One file (1)");

        public static string OpenFile => T("Открыть аудиофайл (Ctrl+O)", "Open audio file (Ctrl+O)");
        public static string Stop => T("Остановить", "Stop");
        public static string PreviousFile => T("Предыдущий файл", "Previous file");
        public static string PlayPause => T("Воспроизведение / Пауза (Пробел)", "Play / Pause (Space)");
        public static string NextFile => T("Следующий файл", "Next file");
        public static string Volume => T("Громкость", "Volume");
        public static string VolumeMute => T("Громкость / Без звука", "Volume / Mute");
        public static string VolumeControl => T("Регулировка громкости", "Volume control");
        public static string ResizeGrip => T("Потяните для изменения масштаба", "Drag to resize");
        public static string DragToReorder => T("Потяните для изменения порядка", "Drag to reorder");
        public static string RemoveFromQueue => T("Удалить из очереди", "Remove from queue");
        public static string RemoveFromPlaylist => T("Удалить из плейлиста", "Remove from playlist");
        public static string ClickToRename => T("Нажмите, чтобы переименовать", "Click to rename");

        // Queue UI
        public static string TabQueue => T("Очередь", "Queue");
        public static string TabPlaylists => T("Плейлисты", "Playlists");
        public static string AddToPlaybackQueueCue => T("Добавить в очередь воспроизведения", "Add to playback queue");

        public static string BtnShuffleQueue => T("🔀 Перемешать", "🔀 Shuffle");
        public static string BtnSaveAsPlaylist => T("★ В плейлист", "★ To playlist");
        public static string BtnExportQueue => T("Экспорт...", "Export...");
        public static string BtnClearQueue => T("Очистить", "Clear");
        public static string BtnAddFiles => T("+ Добавить файлы", "+ Add files");

        // Playlists Master UI
        public static string BtnCreatePlaylist => T("+ Создать плейлист", "+ Create playlist");
        public static string BtnImportM3U8 => T("Импорт M3U8...", "Import M3U8...");
        public static string BtnQueueToPlaylist => T("Очередь → Плейлист", "Queue → Playlist");
        public static string BtnConfirmCreate => T("Создать", "Create");
        public static string BtnCancel => T("Отмена", "Cancel");
        public static string PlayPlaylistTooltip => T("Воспроизвести плейлист", "Play playlist");
        public static string ShufflePlayPlaylistTooltip => T("Перемешать и воспроизвести плейлист", "Shuffle and play playlist");
        public static string DeletePlaylistTooltip => T("Удалить плейлист", "Delete playlist");

        // Playlist Detail UI
        public static string BtnBack => T("← Назад", "← Back");
        public static string BtnPlay => T("▶ Играть", "▶ Play");
        public static string BtnPause => T("⏸ Пауза", "⏸ Pause");
        public static string BtnShufflePlaylist => T("🔀 Перемешать", "🔀 Shuffle");
        public static string ShufflePlaylistOrderTooltip => T("Перемешать порядок файлов в плейлисте", "Shuffle file order in playlist");
        public static string BtnAddToQueue => T("+ В очередь", "+ To queue");
        public static string BtnExportPlaylist => T("Экспорт M3U8...", "Export M3U8...");
        public static string BtnDeletePlaylist => T("Удалить плейлист", "Delete playlist");

        // Dialogs & Messages
        public static string PlaybackError => T("Ошибка воспроизведения", "Playback error");
        public static string QueueEmptyExport => T("Очередь воспроизведения пуста. Нечего экспортировать.", "Playback queue is empty. Nothing to export.");
        public static string QueueEmptySave => T("Очередь воспроизведения пуста. Нечего сохранять в плейлист.", "Playback queue is empty. Nothing to save to playlist.");
        public static string ExportSuccess => T("Успешно экспортировано!", "Successfully exported!");
        public static string ExportTitle => T("Экспорт", "Export");
        public static string PlaylistEmpty => T("В этом плейлисте пока нет файлов.", "There are no files in this playlist yet.");
        public static string DeletePlaylistPrompt => T("Удалить плейлист «{0}»?", "Delete playlist \"{0}\"?");
        public static string DeleteTitle => T("Удаление", "Delete");
        public static string AddedToQueueCount => T("В очередь добавлено {0} файлов.", "Added {0} files to queue.");
        public static string AddedToQueueToast => T("Добавлено в очередь", "Added to queue");
        public static string ImportFailed => T("Не удалось загрузить плейлист или в нем нет поддерживаемых файлов.", "Failed to load playlist or no supported files found.");
        public static string ImportTitle => T("Импорт", "Import");
    }
}
