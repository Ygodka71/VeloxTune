using System;
using System.IO;
using NAudio.Wave;
using Concentus.Structs;
using Concentus.Oggfile;

namespace ModernPlayer.Audio
{
    public class OpusWaveStream : WaveStream
    {
        private readonly object _lock = new();
        private readonly FileStream _fileStream;
        private readonly OpusOggReadStream _readStream;
        private readonly WaveFormat _waveFormat;
        private byte[] _pcmBuffer = Array.Empty<byte>();
        private int _pcmBufferOffset = 0;
        private int _pcmBufferCount = 0;
        private long _position = 0;
        private readonly long _length;
        private bool _isDisposed = false;

        public OpusWaveStream(string filePath)
        {
            _fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            #pragma warning disable CS0618
            var decoder = new OpusDecoder(48000, 2);
            #pragma warning restore CS0618
            _readStream = new OpusOggReadStream(decoder, _fileStream);
            _waveFormat = new WaveFormat(48000, 16, 2);

            if (_readStream.TotalTime.TotalSeconds > 0)
            {
                _length = (long)(_readStream.TotalTime.TotalSeconds * _waveFormat.AverageBytesPerSecond);
            }
            else
            {
                _length = _fileStream.Length;
            }
        }

        public override WaveFormat WaveFormat => _waveFormat;

        public override long Length => _length;

        public override long Position
        {
            get
            {
                lock (_lock)
                {
                    return _position;
                }
            }
            set
            {
                lock (_lock)
                {
                    if (_isDisposed) return;
                    if (_readStream.CanSeek)
                    {
                        var targetTime = TimeSpan.FromSeconds((double)value / _waveFormat.AverageBytesPerSecond);
                        _readStream.SeekTo(targetTime);
                        _pcmBufferOffset = 0;
                        _pcmBufferCount = 0;
                        _position = value;
                    }
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            lock (_lock)
            {
                if (_isDisposed) return 0;
                int totalBytesRead = 0;

                while (totalBytesRead < count)
                {
                    if (_pcmBufferCount > 0)
                    {
                        int bytesToCopy = Math.Min(count - totalBytesRead, _pcmBufferCount);
                        Buffer.BlockCopy(_pcmBuffer, _pcmBufferOffset, buffer, offset + totalBytesRead, bytesToCopy);
                        _pcmBufferOffset += bytesToCopy;
                        _pcmBufferCount -= bytesToCopy;
                        totalBytesRead += bytesToCopy;
                        _position += bytesToCopy;
                    }
                    else
                    {
                        if (!_readStream.HasNextPacket)
                        {
                            break;
                        }

                        short[]? packet = _readStream.DecodeNextPacket();
                        if (packet == null || packet.Length == 0)
                        {
                            break;
                        }

                        int packetByteCount = packet.Length * sizeof(short);
                        if (_pcmBuffer.Length < packetByteCount)
                        {
                            _pcmBuffer = new byte[packetByteCount];
                        }

                        Buffer.BlockCopy(packet, 0, _pcmBuffer, 0, packetByteCount);
                        _pcmBufferOffset = 0;
                        _pcmBufferCount = packetByteCount;
                    }
                }

                return totalBytesRead;
            }
        }

        protected override void Dispose(bool disposing)
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                if (disposing)
                {
                    try { _readStream?.Close(); } catch { }
                    try { _fileStream?.Dispose(); } catch { }
                }
            }
            base.Dispose(disposing);
        }
    }
}
