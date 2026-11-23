using System;
using System.IO;
using System.Threading;
using NAudio.Wave;
using NAudio.Vorbis;

namespace ModernPlayer.Audio
{
    public class AudioPlayerService : IDisposable
    {
        private readonly object _lock = new();
        private WaveOut? _outputDevice;
        private SafeAudioStream? _currentStream;
        private string? _currentFilePath;
        private float _volume = 1.0f;
        private bool _isDisposed;

        public event Action? PlaybackStateChanged;
        public event Action? TrackEnded;
        public event Action<string>? ErrorOccurred;

        public bool IsPlaying
        {
            get
            {
                lock (_lock)
                {
                    return _outputDevice?.PlaybackState == PlaybackState.Playing;
                }
            }
        }

        public bool IsPaused
        {
            get
            {
                lock (_lock)
                {
                    return _outputDevice?.PlaybackState == PlaybackState.Paused;
                }
            }
        }

        public bool IsStopped
        {
            get
            {
                lock (_lock)
                {
                    return _outputDevice == null || _outputDevice.PlaybackState == PlaybackState.Stopped;
                }
            }
        }

        public string? CurrentFilePath
        {
            get
            {
                lock (_lock)
                {
                    return _currentFilePath;
                }
            }
        }

        public bool HasTrack
        {
            get
            {
                lock (_lock)
                {
                    return !string.IsNullOrEmpty(_currentFilePath);
                }
            }
        }

        public TimeSpan CurrentTime
        {
            get
            {
                lock (_lock)
                {
                    if (_currentStream == null) return TimeSpan.Zero;
                    try
                    {
                        return _currentStream.CurrentTime;
                    }
                    catch
                    {
                        return TimeSpan.Zero;
                    }
                }
            }
            set
            {
                lock (_lock)
                {
                    if (_currentStream == null) return;
                    try
                    {
                        bool wasPlaying = _outputDevice?.PlaybackState == PlaybackState.Playing;
                        if (wasPlaying && _outputDevice != null)
                        {
                            _outputDevice.Pause();
                        }

                        _currentStream.SeekTime(value);

                        if (wasPlaying && _outputDevice != null)
                        {
                            _outputDevice.Play();
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        public TimeSpan TotalTime
        {
            get
            {
                lock (_lock)
                {
                    if (_currentStream == null) return TimeSpan.Zero;
                    try
                    {
                        return _currentStream.TotalTime;
                    }
                    catch
                    {
                        return TimeSpan.Zero;
                    }
                }
            }
        }

        public float Volume
        {
            get => _volume;
            set
            {
                lock (_lock)
                {
                    _volume = Math.Clamp(value, 0f, 1f);
                    if (_outputDevice != null)
                    {
                        try
                        {
                            _outputDevice.Volume = _volume;
                        }
                        catch
                        {
                        }
                    }
                }
            }
        }

        public bool LoadFile(string filePath)
        {
            if (!File.Exists(filePath))
            {
                ErrorOccurred?.Invoke($"Файл не найден: {filePath}");
                return false;
            }

            lock (_lock)
            {
                try
                {
                    CleanCurrentStream();

                    var newStream = CreateWaveStream(filePath);
                    if (newStream == null)
                    {
                        ErrorOccurred?.Invoke($"Не удалось открыть аудиофайл: {Path.GetFileName(filePath)}");
                        return false;
                    }

                    var newDevice = new WaveOut();
                    newDevice.Volume = _volume;
                    newDevice.Init(newStream);
                    newDevice.PlaybackStopped += OnPlaybackStopped;

                    _currentStream = newStream;
                    _outputDevice = newDevice;
                    _currentFilePath = filePath;

                    PlaybackStateChanged?.Invoke();
                    return true;
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"Ошибка загрузки файла: {ex.Message}");
                    return false;
                }
            }
        }

        private SafeAudioStream? CreateWaveStream(string filePath)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            WaveStream? stream = null;

            // 1. OGG Vorbis
            if (ext == ".ogg")
            {
                try
                {
                    stream = new VorbisWaveReader(filePath);
                }
                catch
                {
                }
            }
            // 2. Opus
            else if (ext == ".opus")
            {
                try
                {
                    stream = new OpusWaveStream(filePath);
                }
                catch
                {
                }
            }
            // 3. AAC, M4A, MP4
            else if (ext == ".m4a" || ext == ".aac" || ext == ".mp4")
            {
                try
                {
                    stream = new MediaFoundationReader(filePath);
                }
                catch
                {
                }
            }
            // 4. FLAC
            else if (ext == ".flac")
            {
                try
                {
                    stream = new MediaFoundationReader(filePath);
                }
                catch
                {
                    try { stream = new AudioFileReader(filePath); } catch { }
                }
            }

            // 5. MP3, WAV and general fallback
            if (stream == null)
            {
                try
                {
                    stream = new AudioFileReader(filePath);
                }
                catch
                {
                    try { stream = new MediaFoundationReader(filePath); } catch { }
                }
            }

            if (stream == null) return null;

            return new SafeAudioStream(stream);
        }

        public void Play()
        {
            lock (_lock)
            {
                if (_outputDevice != null && _currentStream != null)
                {
                    try
                    {
                        _outputDevice.Play();
                        PlaybackStateChanged?.Invoke();
                    }
                    catch
                    {
                    }
                }
            }
        }

        public void Pause()
        {
            lock (_lock)
            {
                if (_outputDevice != null && _outputDevice.PlaybackState == PlaybackState.Playing)
                {
                    try
                    {
                        _outputDevice.Pause();
                        PlaybackStateChanged?.Invoke();
                    }
                    catch
                    {
                    }
                }
            }
        }

        public void TogglePlayPause()
        {
            if (IsPlaying)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (_outputDevice != null)
                {
                    try
                    {
                        _outputDevice.Stop();
                        if (_currentStream != null)
                        {
                            _currentStream.Position = 0;
                        }
                    }
                    catch
                    {
                    }
                    PlaybackStateChanged?.Invoke();
                }
            }
        }

        private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
        {
            lock (_lock)
            {
                // Only process event if it comes from the ACTIVE output device
                if (sender != _outputDevice || _outputDevice == null || _isDisposed)
                {
                    return;
                }

                PlaybackStateChanged?.Invoke();

                if (e.Exception != null)
                {
                    ErrorOccurred?.Invoke($"Ошибка воспроизведения: {e.Exception.Message}");
                    return;
                }

                if (_currentStream != null && TotalTime.TotalSeconds > 0)
                {
                    if (CurrentTime >= TotalTime - TimeSpan.FromMilliseconds(500))
                    {
                        TrackEnded?.Invoke();
                    }
                }
            }
        }

        private void CleanCurrentStream()
        {
            lock (_lock)
            {
                var oldDevice = _outputDevice;
                var oldStream = _currentStream;

                _outputDevice = null;
                _currentStream = null;
                _currentFilePath = null;

                if (oldDevice != null)
                {
                    try
                    {
                        oldDevice.PlaybackStopped -= OnPlaybackStopped;
                        oldDevice.Stop();
                    }
                    catch
                    {
                    }
                }

                if (oldDevice != null || oldStream != null)
                {
                    // Asynchronously dispose old device and stream to prevent UI lockup and memory leaks
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            Thread.Sleep(30);
                            oldDevice?.Dispose();
                        }
                        catch
                        {
                        }

                        try
                        {
                            oldStream?.Dispose();
                        }
                        catch
                        {
                        }
                    });
                }
            }
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                _isDisposed = true;
                CleanCurrentStream();
            }
        }
    }
}
