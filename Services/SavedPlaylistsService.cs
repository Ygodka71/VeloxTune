using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ModernPlayer.Services
{
    public class PlaylistSong : INotifyPropertyChanged
    {
        private string _filePath = string.Empty;
        private string _title = string.Empty;
        private string _artist = string.Empty;
        private TimeSpan _duration = TimeSpan.Zero;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilePath))); }
        }

        public string Title
        {
            get => _title;
            set { _title = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title))); }
        }

        public string Artist
        {
            get => _artist;
            set { _artist = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Artist))); }
        }

        public TimeSpan Duration
        {
            get => _duration;
            set { _duration = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Duration))); }
        }
    }

    public class LocalPlaylist : INotifyPropertyChanged
    {
        private string _name = "Новый плейлист";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FormattedInfo)));
                }
            }
        }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public ObservableCollection<PlaylistSong> Songs { get; set; } = new();

        public int TrackCount => Songs.Count;

        public string FormattedInfo
        {
            get
            {
                if (LocalizationService.IsRussian)
                {
                    int c = Songs.Count;
                    string suffix = (c % 10 == 1 && c % 100 != 11) ? "файл" :
                                    (c % 10 >= 2 && c % 10 <= 4 && (c % 100 < 10 || c % 100 >= 20)) ? "файла" : "файлов";
                    return $"{c} {suffix} • {CreatedAt:dd.MM.yyyy}";
                }
                else
                {
                    int c = Songs.Count;
                    return $"{c} file{(c == 1 ? "" : "s")} • {CreatedAt:dd.MM.yyyy}";
                }
            }
        }
    }

    public class SavedPlaylistsService
    {
        private static readonly string StorageDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ModernPlayer");

        private static readonly string StorageFilePath = Path.Combine(StorageDir, "playlists.json");

        public ObservableCollection<LocalPlaylist> Playlists { get; } = new();

        public event Action? PlaylistsChanged;

        public SavedPlaylistsService()
        {
            Load();
        }

        public void Load()
        {
            try
            {
                Playlists.Clear();

                string filePath = StorageFilePath;
                if (!File.Exists(filePath))
                {
                    // Fallback to local app directory if present
                    string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "playlists.json");
                    if (File.Exists(localPath)) filePath = localPath;
                }

                if (File.Exists(filePath))
                {
                    string json = File.ReadAllText(filePath, Encoding.UTF8);
                    var loaded = JsonSerializer.Deserialize<List<LocalPlaylist>>(json);
                    if (loaded != null)
                    {
                        foreach (var pl in loaded)
                        {
                            Playlists.Add(pl);
                        }
                    }
                }
            }
            catch
            {
            }
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(StorageDir))
                {
                    Directory.CreateDirectory(StorageDir);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };

                string json = JsonSerializer.Serialize(Playlists.ToList(), options);
                File.WriteAllText(StorageFilePath, json, Encoding.UTF8);

                PlaylistsChanged?.Invoke();
            }
            catch
            {
            }
        }

        public LocalPlaylist CreatePlaylist(string name)
        {
            string finalName = string.IsNullOrWhiteSpace(name) ? "Новый плейлист" : name.Trim();
            var pl = new LocalPlaylist
            {
                Name = finalName,
                CreatedAt = DateTime.Now
            };

            Playlists.Insert(0, pl);
            Save();
            return pl;
        }

        public void DeletePlaylist(LocalPlaylist playlist)
        {
            if (Playlists.Remove(playlist))
            {
                Save();
            }
        }

        public void RenamePlaylist(LocalPlaylist playlist, string newName)
        {
            if (!string.IsNullOrWhiteSpace(newName))
            {
                playlist.Name = newName.Trim();
                Save();
            }
        }

        public void ShufflePlaylist(LocalPlaylist playlist)
        {
            if (playlist.Songs.Count <= 1) return;
            var rng = new Random();
            var list = playlist.Songs.ToList();
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = rng.Next(n + 1);
                (list[k], list[n]) = (list[n], list[k]);
            }
            playlist.Songs.Clear();
            foreach (var s in list)
            {
                playlist.Songs.Add(s);
            }
            Save();
        }

        public void AddSongs(LocalPlaylist playlist, IEnumerable<string> filePaths)
        {
            foreach (var path in filePaths)
            {
                if (File.Exists(path) && PlaylistService.IsSupportedFile(path))
                {
                    var meta = ArtworkService.ExtractMetadata(path);
                    playlist.Songs.Add(new PlaylistSong
                    {
                        FilePath = path,
                        Title = meta.Title,
                        Artist = meta.Artist,
                        Duration = meta.Duration
                    });
                }
            }
            Save();
        }

        public void RemoveSong(LocalPlaylist playlist, PlaylistSong song)
        {
            if (playlist.Songs.Remove(song))
            {
                Save();
            }
        }

        public void MoveSong(LocalPlaylist playlist, int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= playlist.Songs.Count ||
                newIndex < 0 || newIndex >= playlist.Songs.Count ||
                oldIndex == newIndex)
            {
                return;
            }

            playlist.Songs.Move(oldIndex, newIndex);
            Save();
        }

        public void MovePlaylist(int oldIndex, int newIndex)
        {
            if (oldIndex < 0 || oldIndex >= Playlists.Count ||
                newIndex < 0 || newIndex >= Playlists.Count ||
                oldIndex == newIndex)
            {
                return;
            }

            Playlists.Move(oldIndex, newIndex);
            Save();
        }

        public LocalPlaylist CreateFromQueue(string? name, IEnumerable<PlaylistItem> queueItems)
        {
            string plName = string.IsNullOrWhiteSpace(name)
                ? $"Очередь ({DateTime.Now:dd.MM HH:mm})"
                : name.Trim();

            var pl = new LocalPlaylist
            {
                Name = plName,
                CreatedAt = DateTime.Now
            };

            foreach (var item in queueItems)
            {
                pl.Songs.Add(new PlaylistSong
                {
                    FilePath = item.FilePath,
                    Title = item.Title,
                    Artist = item.Artist,
                    Duration = item.Duration
                });
            }

            Playlists.Insert(0, pl);
            Save();
            return pl;
        }

        public bool ExportToM3u8(LocalPlaylist playlist, string targetFilePath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("#EXTM3U");

                foreach (var song in playlist.Songs)
                {
                    int sec = (int)song.Duration.TotalSeconds;
                    string title = string.IsNullOrWhiteSpace(song.Artist) ? song.Title : $"{song.Artist} - {song.Title}";
                    sb.AppendLine($"#EXTINF:{sec},{title}");
                    sb.AppendLine(song.FilePath);
                }

                File.WriteAllText(targetFilePath, sb.ToString(), Encoding.UTF8);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public LocalPlaylist? ImportFromM3u(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                string baseDir = Path.GetDirectoryName(filePath) ?? string.Empty;
                var lines = File.ReadAllLines(filePath, Encoding.UTF8);
                string plName = Path.GetFileNameWithoutExtension(filePath);

                var pl = new LocalPlaylist
                {
                    Name = plName,
                    CreatedAt = DateTime.Now
                };

                foreach (var rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                    string fullPath = line;
                    if (!Path.IsPathRooted(fullPath))
                    {
                        fullPath = Path.Combine(baseDir, fullPath);
                    }

                    if (File.Exists(fullPath) && PlaylistService.IsSupportedFile(fullPath))
                    {
                        var meta = ArtworkService.ExtractMetadata(fullPath);
                        pl.Songs.Add(new PlaylistSong
                        {
                            FilePath = fullPath,
                            Title = meta.Title,
                            Artist = meta.Artist,
                            Duration = meta.Duration
                        });
                    }
                }

                if (pl.Songs.Count > 0)
                {
                    Playlists.Insert(0, pl);
                    Save();
                    return pl;
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
