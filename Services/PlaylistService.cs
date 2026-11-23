using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;

namespace ModernPlayer.Services
{
    public enum RepeatMode
    {
        Off,
        All,
        One
    }

    public class PlaylistItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;
    }

    public class PlaylistService
    {
        public static readonly string[] SupportedExtensions = 
        {
            ".mp3", ".m4a", ".aac", ".mp4", ".flac", ".wav", ".ogg", ".opus"
        };

        private readonly List<PlaylistItem> _items = new();
        private readonly List<int> _shuffleOrder = new();
        private int _currentIndex = -1;
        private int _shuffleIndex = -1;
        private readonly Random _random = new();

        public ObservableCollection<PlaylistItem> Items { get; } = new();

        public RepeatMode Repeat { get; set; } = RepeatMode.Off;
        public bool IsShuffle { get; set; } = false;

        public event Action<PlaylistItem?>? CurrentTrackChanged;
        public event Action? PlaylistChanged;

        public int Count => _items.Count;
        public int CurrentIndex => _currentIndex;
        public PlaylistItem? CurrentItem => (_currentIndex >= 0 && _currentIndex < _items.Count) ? _items[_currentIndex] : null;

        public static bool IsSupportedFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return SupportedExtensions.Contains(ext);
        }

        public static bool IsPlaylistFile(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            return ext == ".m3u" || ext == ".m3u8";
        }

        /// <summary>
        /// Plays a single file, replacing anything currently in the queue.
        /// Does NOT automatically scan or add any other files from the folder!
        /// </summary>
        public void PlaySingleFile(string filePath)
        {
            _items.Clear();
            Items.Clear();

            if (File.Exists(filePath) && IsSupportedFile(filePath))
            {
                var item = new PlaylistItem
                {
                    FilePath = filePath,
                    Title = Path.GetFileNameWithoutExtension(filePath)
                };
                _items.Add(item);
                Items.Add(item);
                RebuildShuffle();
                SetIndex(0);
            }
            else
            {
                SetIndex(-1);
            }

            PlaylistChanged?.Invoke();
        }

        public void AddFiles(IEnumerable<string> filePaths, bool autoPlay = false)
        {
            foreach (var path in filePaths)
            {
                if (IsSupportedFile(path) && File.Exists(path))
                {
                    var item = new PlaylistItem
                    {
                        FilePath = path,
                        Title = Path.GetFileNameWithoutExtension(path)
                    };
                    _items.Add(item);
                    Items.Add(item);
                }
            }

            RebuildShuffle();
            PlaylistChanged?.Invoke();

            if (autoPlay && _currentIndex == -1 && _items.Count > 0)
            {
                SetIndex(0);
            }
        }

        public void AddFile(string filePath, bool autoPlay = false)
        {
            AddFiles(new[] { filePath }, autoPlay);
        }

        public void AddItem(string filePath, bool autoPlay = false)
        {
            AddFiles(new[] { filePath }, autoPlay);
        }

        public void Clear()
        {
            _items.Clear();
            Items.Clear();
            _currentIndex = -1;
            _shuffleIndex = -1;
            _shuffleOrder.Clear();
            CurrentTrackChanged?.Invoke(null);
            PlaylistChanged?.Invoke();
        }

        public void RemoveItem(PlaylistItem item)
        {
            int index = _items.IndexOf(item);
            if (index >= 0)
            {
                RemoveAt(index);
            }
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _items.Count) return;

            bool isRemovingCurrent = (index == _currentIndex);

            _items.RemoveAt(index);
            Items.RemoveAt(index);

            if (_items.Count == 0)
            {
                _currentIndex = -1;
                CurrentTrackChanged?.Invoke(null);
            }
            else if (isRemovingCurrent)
            {
                int newIndex = Math.Min(index, _items.Count - 1);
                SetIndex(newIndex);
            }
            else if (index < _currentIndex)
            {
                _currentIndex--;
            }

            RebuildShuffle();
            PlaylistChanged?.Invoke();
        }

        public void MoveItem(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= _items.Count ||
                newIndex < 0 || newIndex >= _items.Count ||
                oldIndex == newIndex)
            {
                return;
            }

            var item = _items[oldIndex];
            _items.RemoveAt(oldIndex);
            _items.Insert(newIndex, item);

            Items.Move(oldIndex, newIndex);

            if (_currentIndex == oldIndex)
            {
                _currentIndex = newIndex;
            }
            else if (_currentIndex > oldIndex && _currentIndex <= newIndex)
            {
                _currentIndex--;
            }
            else if (_currentIndex < oldIndex && _currentIndex >= newIndex)
            {
                _currentIndex++;
            }

            RebuildShuffle();
            PlaylistChanged?.Invoke();
        }

        public void ShuffleQueue()
        {
            if (_items.Count <= 1) return;

            var currentItem = CurrentItem;

            for (int i = _items.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                var temp = _items[i];
                _items[i] = _items[j];
                _items[j] = temp;
            }

            Items.Clear();
            foreach (var item in _items)
            {
                Items.Add(item);
            }

            if (currentItem != null)
            {
                _currentIndex = _items.IndexOf(currentItem);
            }

            RebuildShuffle();
            PlaylistChanged?.Invoke();
        }

        public void LoadFromLocalPlaylist(LocalPlaylist playlist, int startIndex = 0)
        {
            _items.Clear();
            Items.Clear();

            foreach (var song in playlist.Songs)
            {
                if (File.Exists(song.FilePath) && IsSupportedFile(song.FilePath))
                {
                    var item = new PlaylistItem
                    {
                        FilePath = song.FilePath,
                        Title = song.Title,
                        Artist = song.Artist,
                        Duration = song.Duration
                    };
                    _items.Add(item);
                    Items.Add(item);
                }
            }

            RebuildShuffle();
            if (_items.Count > 0 && startIndex >= 0 && startIndex < _items.Count)
            {
                SetIndex(startIndex);
            }
            else
            {
                SetIndex(-1);
            }
            PlaylistChanged?.Invoke();
        }

        public bool SavePlaylist(string playlistFilePath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("#EXTM3U");

                foreach (var item in _items)
                {
                    int seconds = (int)item.Duration.TotalSeconds;
                    sb.AppendLine($"#EXTINF:{seconds},{item.Title}");
                    sb.AppendLine(item.FilePath);
                }

                File.WriteAllText(playlistFilePath, sb.ToString(), Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool LoadPlaylist(string playlistFilePath)
        {
            if (!File.Exists(playlistFilePath)) return false;

            try
            {
                string baseDir = Path.GetDirectoryName(playlistFilePath) ?? string.Empty;
                var lines = File.ReadAllLines(playlistFilePath, Encoding.UTF8);

                var loadedPaths = new List<string>();

                foreach (var rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                    string fullPath = line;
                    if (!Path.IsPathRooted(fullPath))
                    {
                        fullPath = Path.Combine(baseDir, fullPath);
                    }

                    if (File.Exists(fullPath) && IsSupportedFile(fullPath))
                    {
                        loadedPaths.Add(Path.GetFullPath(fullPath));
                    }
                }

                if (loadedPaths.Count > 0)
                {
                    _items.Clear();
                    Items.Clear();

                    foreach (var path in loadedPaths)
                    {
                        var item = new PlaylistItem
                        {
                            FilePath = path,
                            Title = Path.GetFileNameWithoutExtension(path)
                        };
                        _items.Add(item);
                        Items.Add(item);
                    }

                    RebuildShuffle();
                    SetIndex(0);
                    PlaylistChanged?.Invoke();
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public void SetIndex(int index)
        {
            if (index < 0 || index >= _items.Count)
            {
                _currentIndex = -1;
                CurrentTrackChanged?.Invoke(null);
                return;
            }

            _currentIndex = index;
            if (IsShuffle && _shuffleOrder.Count == _items.Count)
            {
                _shuffleIndex = _shuffleOrder.IndexOf(_currentIndex);
            }
            CurrentTrackChanged?.Invoke(CurrentItem);
        }

        public PlaylistItem? Next(bool isAutoAdvance = false)
        {
            if (_items.Count == 0) return null;

            if (Repeat == RepeatMode.One && isAutoAdvance)
            {
                CurrentTrackChanged?.Invoke(CurrentItem);
                return CurrentItem;
            }

            if (IsShuffle)
            {
                if (_shuffleOrder.Count != _items.Count) RebuildShuffle();
                _shuffleIndex++;
                if (_shuffleIndex >= _shuffleOrder.Count)
                {
                    if (Repeat == RepeatMode.All)
                    {
                        RebuildShuffle();
                        _shuffleIndex = 0;
                    }
                    else
                    {
                        _shuffleIndex = _shuffleOrder.Count - 1;
                        if (isAutoAdvance) return null;
                    }
                }
                int newIdx = _shuffleOrder[_shuffleIndex];
                SetIndex(newIdx);
                return CurrentItem;
            }
            else
            {
                int newIdx = _currentIndex + 1;
                if (newIdx >= _items.Count)
                {
                    if (Repeat == RepeatMode.All)
                    {
                        newIdx = 0;
                    }
                    else
                    {
                        if (isAutoAdvance) return null;
                        newIdx = _items.Count - 1;
                    }
                }
                SetIndex(newIdx);
                return CurrentItem;
            }
        }

        public PlaylistItem? Previous()
        {
            if (_items.Count == 0) return null;

            if (IsShuffle)
            {
                if (_shuffleOrder.Count != _items.Count) RebuildShuffle();
                _shuffleIndex--;
                if (_shuffleIndex < 0)
                {
                    _shuffleIndex = Repeat == RepeatMode.All ? _shuffleOrder.Count - 1 : 0;
                }
                int newIdx = _shuffleOrder[_shuffleIndex];
                SetIndex(newIdx);
                return CurrentItem;
            }
            else
            {
                int newIdx = _currentIndex - 1;
                if (newIdx < 0)
                {
                    newIdx = Repeat == RepeatMode.All ? _items.Count - 1 : 0;
                }
                SetIndex(newIdx);
                return CurrentItem;
            }
        }

        public void ToggleShuffle()
        {
            IsShuffle = !IsShuffle;
            if (IsShuffle)
            {
                RebuildShuffle();
                if (_currentIndex >= 0 && _shuffleOrder.Count > 0)
                {
                    _shuffleIndex = _shuffleOrder.IndexOf(_currentIndex);
                }
            }
        }

        public void CycleRepeat()
        {
            Repeat = Repeat switch
            {
                RepeatMode.Off => RepeatMode.All,
                RepeatMode.All => RepeatMode.One,
                _ => RepeatMode.Off
            };
        }

        private void RebuildShuffle()
        {
            _shuffleOrder.Clear();
            for (int i = 0; i < _items.Count; i++) _shuffleOrder.Add(i);

            for (int i = _shuffleOrder.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                int tmp = _shuffleOrder[i];
                _shuffleOrder[i] = _shuffleOrder[j];
                _shuffleOrder[j] = tmp;
            }

            _shuffleIndex = _currentIndex >= 0 ? _shuffleOrder.IndexOf(_currentIndex) : 0;
        }
    }
}
