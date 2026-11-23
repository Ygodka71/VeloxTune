using System;
using NAudio.Wave;
using NAudio.Vorbis;

namespace ModernPlayer.Audio
{
    /// <summary>
    /// Thread-safe wrapper around any WaveStream.
    /// Prevents AccessViolationException and ObjectDisposedException when audio playback threads
    /// are reading buffers while the UI thread is switching tracks or disposing the stream.
    /// </summary>
    public class SafeAudioStream : WaveStream
    {
        private readonly WaveStream _inner;
        private readonly object _syncLock = new();
        private volatile bool _isDisposed;

        public SafeAudioStream(WaveStream inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public override WaveFormat WaveFormat => _inner.WaveFormat;

        public override long Length
        {
            get
            {
                if (_isDisposed) return 0;
                lock (_syncLock)
                {
                    if (_isDisposed) return 0;
                    try { return _inner.Length; } catch { return 0; }
                }
            }
        }

        public override long Position
        {
            get
            {
                if (_isDisposed) return 0;
                lock (_syncLock)
                {
                    if (_isDisposed) return 0;
                    try { return _inner.Position; } catch { return 0; }
                }
            }
            set
            {
                if (_isDisposed) return;
                lock (_syncLock)
                {
                    if (_isDisposed) return;
                    try { _inner.Position = value; } catch { }
                }
            }
        }

        public override TimeSpan CurrentTime
        {
            get
            {
                if (_isDisposed) return TimeSpan.Zero;
                lock (_syncLock)
                {
                    if (_isDisposed) return TimeSpan.Zero;
                    try { return _inner.CurrentTime; } catch { return TimeSpan.Zero; }
                }
            }
            set
            {
                SeekTime(value);
            }
        }

        public override TimeSpan TotalTime
        {
            get
            {
                if (_isDisposed) return TimeSpan.Zero;
                lock (_syncLock)
                {
                    if (_isDisposed) return TimeSpan.Zero;
                    try { return _inner.TotalTime; } catch { return TimeSpan.Zero; }
                }
            }
        }

        public override bool CanRead => !_isDisposed && _inner.CanRead;
        public override bool CanSeek => !_isDisposed && _inner.CanSeek;

        public void SeekTime(TimeSpan time)
        {
            if (_isDisposed) return;
            lock (_syncLock)
            {
                if (_isDisposed) return;
                try
                {
                    if (time < TimeSpan.Zero) time = TimeSpan.Zero;
                    var total = _inner.TotalTime;
                    if (total > TimeSpan.Zero && time > total) time = total;

                    if (_inner is VorbisWaveReader vorbis)
                    {
                        vorbis.CurrentTime = time;
                    }
                    else if (_inner is OpusWaveStream opus)
                    {
                        opus.Position = (long)(time.TotalSeconds * opus.WaveFormat.AverageBytesPerSecond);
                    }
                    else
                    {
                        int blockAlign = _inner.WaveFormat.BlockAlign;
                        long bytes = (long)(time.TotalSeconds * _inner.WaveFormat.AverageBytesPerSecond);
                        if (blockAlign > 0)
                        {
                            bytes -= (bytes % blockAlign);
                        }
                        bytes = Math.Clamp(bytes, 0, _inner.Length);
                        _inner.Position = bytes;
                    }
                }
                catch
                {
                    try { _inner.CurrentTime = time; } catch { }
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_isDisposed)
            {
                Array.Clear(buffer, offset, count);
                return 0;
            }

            lock (_syncLock)
            {
                if (_isDisposed)
                {
                    Array.Clear(buffer, offset, count);
                    return 0;
                }

                try
                {
                    return _inner.Read(buffer, offset, count);
                }
                catch
                {
                    Array.Clear(buffer, offset, count);
                    return 0;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            _isDisposed = true;
            lock (_syncLock)
            {
                try
                {
                    _inner.Dispose();
                }
                catch
                {
                }
            }
            base.Dispose(disposing);
        }
    }
}
