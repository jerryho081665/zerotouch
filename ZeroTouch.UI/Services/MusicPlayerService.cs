using LibVLCSharp.Shared;
using System;
using System.Collections.Generic;

namespace ZeroTouch.UI.Services
{
    public class MusicPlayerService : IDisposable
    {
        private readonly LibVLC _libVLC;
        private readonly MediaPlayer _mediaPlayer;

        private readonly List<string> _playlist = new();
        private int _currentIndex = 0;
        private Media? _currentMedia;

        public event Action<long, long>? PositionChanged;
        public string CurrentSongName => _playlist.Count > 0
            ? System.IO.Path.GetFileName(_playlist[_currentIndex])
            : string.Empty;

        public MusicPlayerService()
        {
            var libVlcOptions = new[]
            {
                "--no-video",
                "--no-video-title-show",
                "--no-spu",
                "--file-caching=1000",
                "--audio-resampler=soxr",
                "--avcodec-threads=4",
                "--aout=wasapi"
            };

            _libVLC = new LibVLC(libVlcOptions);
            _mediaPlayer = new MediaPlayer(_libVLC);

            _mediaPlayer.TimeChanged += (sender, e) =>
            {
                PositionChanged?.Invoke(_mediaPlayer.Time, _mediaPlayer.Length);
            };
        }

        public void SetPlaylist(IEnumerable<string> songs)
        {
            _playlist.Clear();
            _playlist.AddRange(songs);
            _currentIndex = 0;
        }

        public void Play()
        {
            if (_playlist.Count == 0) return;
            
            if (_mediaPlayer.Media != null)
            {
                _mediaPlayer.Play(); // Resume if paused
            }
            else
            {
                Play(_playlist[_currentIndex]);
            }
        }

        public void Play(string path)
        {
            _currentMedia?.Dispose();
            _currentMedia = new Media(_libVLC, path, FromType.FromPath);
            _mediaPlayer.Media = _currentMedia;
            _mediaPlayer.Play();
        }

        public void Pause()
        {
            if (_mediaPlayer.CanPause)
            {
                _mediaPlayer.Pause();
            }
        }

        public void Stop() => _mediaPlayer.Stop();

        public void Next()
        {
            if (_playlist.Count == 0) return;
            _currentIndex = (_currentIndex + 1) % _playlist.Count;
            Play(_playlist[_currentIndex]);
        }

        public void Previous()
        {
            if (_playlist.Count == 0) return;
            _currentIndex = (_currentIndex - 1 + _playlist.Count) % _playlist.Count;
            Play(_playlist[_currentIndex]);
        }

        public void Seek(long ms)
        {
            _mediaPlayer.Time = ms;
        }

        public void Dispose()
        {
            _currentMedia?.Dispose();
            _mediaPlayer.Dispose();
            _libVLC.Dispose();
        }
    }
}
