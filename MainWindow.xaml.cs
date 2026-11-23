using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using ModernPlayer.Audio;
using ModernPlayer.Services;

namespace ModernPlayer
{
    public partial class MainWindow : Window
    {
        private readonly AudioPlayerService _audioPlayer = new();
        private readonly PlaylistService _playlistService = new();
        private readonly SavedPlaylistsService _savedPlaylistsService = new();
        private readonly DispatcherTimer _timer = new();
        private readonly DispatcherTimer _seekHoverTimer = new();
        private readonly DispatcherTimer _skipDebounceTimer = new();

        private bool _isUserSeeking = false;
        private DateTime _lastSeekTime = DateTime.MinValue;
        private float _previousVolume = 1.0f;
        private string[]? _startupArgs;

        private Point _lastSeekHoverPos;
        private bool _isSeekHoverActive = false;

        private Point _headerMouseDownPos;
        private bool _isHeaderMouseDown = false;
        private double _savedNormalWidth = 500;
        private double _savedNormalHeight = 640;
        private DispatcherTimer? _toastTimer;
        private Action? _modalConfirmAction;

        private LocalPlaylist? _selectedPlaylist;

        // Queue Drag & Drop & Click state
        private Point _queueDragStart;
        private PlaylistItem? _draggedQueueItem;
        private bool _isQueueItemClicked = false;
        private int _queueReorderTargetIndex = -1;

        // Playlist Songs Drag & Drop & Click state
        private Point _playlistSongDragStart;
        private PlaylistSong? _draggedPlaylistSong;
        private bool _isPlaylistSongClicked = false;
        private int _playlistSongReorderTargetIndex = -1;

        private PlaylistItem? _pendingSkipItem;

        #region Win32 Window Resizing
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T typed) return typed;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        private static T? FindVisualChild<T>(DependencyObject? parent, string? name = null) where T : FrameworkElement
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed && (name == null || typed.Name == name))
                {
                    return typed;
                }
                var found = FindVisualChild<T>(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void ResizeWindow(int hitTest)
        {
            if (WindowState == WindowState.Maximized) return;
            var helper = new WindowInteropHelper(this);
            ReleaseCapture();
            SendMessage(helper.Handle, WM_NCLBUTTONDOWN, (IntPtr)hitTest, IntPtr.Zero);
        }

        private void Resize_Left(object sender, MouseButtonEventArgs e) => ResizeWindow(HTLEFT);
        private void Resize_Right(object sender, MouseButtonEventArgs e) => ResizeWindow(HTRIGHT);
        private void Resize_Top(object sender, MouseButtonEventArgs e) => ResizeWindow(HTTOP);
        private void Resize_Bottom(object sender, MouseButtonEventArgs e) => ResizeWindow(HTBOTTOM);
        private void Resize_TopLeft(object sender, MouseButtonEventArgs e) => ResizeWindow(HTTOPLEFT);
        private void Resize_TopRight(object sender, MouseButtonEventArgs e) => ResizeWindow(HTTOPRIGHT);
        private void Resize_BottomLeft(object sender, MouseButtonEventArgs e) => ResizeWindow(HTBOTTOMLEFT);
        private void Resize_BottomRight(object sender, MouseButtonEventArgs e) => ResizeWindow(HTBOTTOMRIGHT);
        #endregion

        public MainWindow(string[]? args = null)
        {
            InitializeComponent();

            _startupArgs = args;

            // Bind playlist to ListBox
            LstPlaylist.ItemsSource = _playlistService.Items;
            LstSavedPlaylists.ItemsSource = _savedPlaylistsService.Playlists;
            _savedPlaylistsService.PlaylistsChanged += OnSavedPlaylistsChanged;

            // Wire audio events
            _audioPlayer.PlaybackStateChanged += OnPlaybackStateChanged;
            _audioPlayer.TrackEnded += OnTrackEnded;
            _audioPlayer.ErrorOccurred += OnAudioError;

            // Wire playlist events
            _playlistService.CurrentTrackChanged += OnCurrentTrackChanged;
            _playlistService.PlaylistChanged += OnPlaylistChanged;

            // Wire Single Instance IPC event (replaces song when opening another file)
            SingleInstanceManager.FileReceived += OnExternalFileReceived;

            // Playback progress timer
            _timer.Interval = TimeSpan.FromMilliseconds(150);
            _timer.Tick += Timer_Tick;
            _timer.Start();

            // Seek hover preview timer (shows timestamp after 500ms hover on timeline slider)
            _seekHoverTimer.Interval = TimeSpan.FromMilliseconds(500);
            _seekHoverTimer.Tick += SeekHoverTimer_Tick;

            // Skip debounce timer to safeguard against rapid next/prev crashes
            _skipDebounceTimer.Interval = TimeSpan.FromMilliseconds(120);
            _skipDebounceTimer.Tick += SkipDebounceTimer_Tick;

            // Set initial default volume
            SliderVolume.Value = _audioPlayer.Volume;

            ApplyLocalization();
            UpdateCounts();
            UpdateRepeatUi();

            SizeChanged += (s, e) =>
            {
                if (WindowState == WindowState.Normal)
                {
                    if (e.NewSize.Width > 150) _savedNormalWidth = e.NewSize.Width;
                    if (e.NewSize.Height > 150) _savedNormalHeight = e.NewSize.Height;
                }
            };

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
        }

        private void ApplyLocalization()
        {
            Title = LocalizationService.AppTitle;
            TxtTitle.Text = LocalizationService.NoFile;
            TxtArtist.Text = LocalizationService.OpenFileHint;

            // Header tooltips
            BtnMaximize.ToolTip = WindowState == WindowState.Maximized ? LocalizationService.Restore : LocalizationService.Maximize;
            BtnPlaylistsToggle.ToolTip = LocalizationService.Playlists;
            BtnPlaylistToggle.ToolTip = LocalizationService.PlaybackQueue;
            BtnOpenFile.ToolTip = LocalizationService.OpenFile;
            BtnStop.ToolTip = LocalizationService.Stop;
            BtnPrevious.ToolTip = LocalizationService.PreviousFile;
            BtnPlayPause.ToolTip = LocalizationService.PlayPause;
            BtnNext.ToolTip = LocalizationService.NextFile;
            BtnVolumeMute.ToolTip = LocalizationService.VolumeMute;
            SliderVolume.ToolTip = LocalizationService.VolumeControl;

            // Drawer tooltips & text
            BtnDrawerClose.ToolTip = LocalizationService.ClosePanel;
            TxtTabQueueLabel.Text = LocalizationService.TabQueue;
            TxtTabPlaylistsLabel.Text = LocalizationService.TabPlaylists;
            TxtDropCueLabel.Text = LocalizationService.AddToPlaybackQueueCue;

            // Queue Footer
            BtnShuffleQueue.Content = LocalizationService.BtnShuffleQueue;
            BtnSaveQueueAsPlaylist.Content = LocalizationService.BtnSaveAsPlaylist;
            BtnSavePlaylist.Content = LocalizationService.BtnExportQueue;
            BtnClearPlaylist.Content = LocalizationService.BtnClearQueue;
            BtnAddFiles.Content = LocalizationService.BtnAddFiles;

            // Playlists Master
            BtnShowCreatePlaylist.Content = LocalizationService.BtnCreatePlaylist;
            BtnImportPlaylist.Content = LocalizationService.BtnImportM3U8;
            BtnQueueToPlaylist.Content = LocalizationService.BtnQueueToPlaylist;
            BtnConfirmCreatePlaylist.Content = LocalizationService.BtnConfirmCreate;
            BtnCancelCreatePlaylist.Content = LocalizationService.BtnCancel;

            // Playlist Detail
            BtnBackToPlaylists.Content = LocalizationService.BtnBack;
            BtnShufflePlaylistOrder.Content = LocalizationService.BtnShufflePlaylist;
            BtnShufflePlaylistOrder.ToolTip = LocalizationService.ShufflePlaylistOrderTooltip;
            BtnAddCurrentPlaylistToQueue.Content = LocalizationService.BtnAddToQueue;
            BtnExportPlaylistDetail.Content = LocalizationService.BtnExportPlaylist;
            BtnDeletePlaylistDetail.Content = LocalizationService.BtnDeletePlaylist;
            BtnAddTracksDetail.Content = LocalizationService.BtnAddFiles;
            PanelPlaylistTitleView.ToolTip = LocalizationService.ClickToRename;

            UpdatePlaylistDetailPlayPauseButton();
            UpdateRepeatUi();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Normal)
            {
                _savedNormalWidth = ActualWidth > 150 ? ActualWidth : 500;
                _savedNormalHeight = ActualHeight > 150 ? ActualHeight : 640;
            }

            // Set window icon safely
            try
            {
                Icon = new BitmapImage(new Uri("pack://application:,,,/icon.ico", UriKind.RelativeOrAbsolute));
            }
            catch
            {
                try { Icon = ArtworkService.GetDefaultCoverArt(); } catch { }
            }

            // Set initial cover art to pixel-art NearestNeighbor
            RenderOptions.SetBitmapScalingMode(ImgCoverArt, BitmapScalingMode.NearestNeighbor);
            ImgCoverArt.Source = ArtworkService.GetDefaultCoverArt();

            // If started with command line arguments (e.g. "Open with...")
            if (_startupArgs != null && _startupArgs.Length > 0)
            {
                string firstFile = _startupArgs[0];
                if (File.Exists(firstFile))
                {
                    if (PlaylistService.IsPlaylistFile(firstFile))
                    {
                        _playlistService.LoadPlaylist(firstFile);
                    }
                    else
                    {
                        _playlistService.PlaySingleFile(firstFile);
                    }
                }
            }

            // Headphone controls & system media key listeners
            InitializeHeadphoneControls();
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null)
            {
                source.AddHook(WndProc);
                RegisterGlobalHotKeys(source.Handle);
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null)
            {
                UnregisterGlobalHotKeys(source.Handle);
            }
            CleanupHeadphoneControls();
            SingleInstanceManager.FileReceived -= OnExternalFileReceived;
            _timer.Stop();
            _seekHoverTimer.Stop();
            _skipDebounceTimer.Stop();
            _audioPlayer.Dispose();
        }

        private void OnExternalFileReceived(string filePath)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        if (PlaylistService.IsPlaylistFile(filePath))
                        {
                            _playlistService.LoadPlaylist(filePath);
                        }
                        else
                        {
                            // Replace playback, clear everything else from queue!
                            _playlistService.PlaySingleFile(filePath);
                        }

                        // Restore window if minimized and bring to foreground
                        if (WindowState == WindowState.Minimized)
                        {
                            WindowState = WindowState.Normal;
                        }
                        Activate();
                        Topmost = true;
                        Topmost = false;
                        Focus();
                    }
                }
                catch
                {
                }
            }));
        }

        private void PlayTrack(PlaylistItem item)
        {
            try
            {
                if (string.IsNullOrEmpty(item.FilePath) || !File.Exists(item.FilePath))
                {
                    return;
                }

                bool loaded = _audioPlayer.LoadFile(item.FilePath);
                if (!loaded) return;

                // Load metadata and artwork
                var meta = ArtworkService.ExtractMetadata(item.FilePath);
                TxtTitle.Text = meta.Title;
                TxtArtist.Text = string.IsNullOrWhiteSpace(meta.Artist)
                    ? (string.IsNullOrWhiteSpace(meta.Album) ? LocalizationService.UnknownArtist : meta.Album)
                    : meta.Artist + (string.IsNullOrWhiteSpace(meta.Album) ? "" : $" • {meta.Album}");

                // Adaptive scaling: NearestNeighbor for pixel art default, HighQuality for actual photos!
                if (meta.HasEmbeddedCover)
                {
                    RenderOptions.SetBitmapScalingMode(ImgCoverArt, BitmapScalingMode.HighQuality);
                    ImgCoverArt.Source = meta.CoverArt;
                }
                else
                {
                    RenderOptions.SetBitmapScalingMode(ImgCoverArt, BitmapScalingMode.NearestNeighbor);
                    ImgCoverArt.Source = ArtworkService.GetDefaultCoverArt();
                }

                // Sync seek bar
                SliderSeek.Minimum = 0;
                double totalSeconds = _audioPlayer.TotalTime.TotalSeconds;
                SliderSeek.Maximum = totalSeconds > 0 ? totalSeconds : 100;
                SliderSeek.Value = 0;

                TxtCurrentTime.Text = FormatTime(TimeSpan.Zero);
                TxtTotalTime.Text = FormatTime(_audioPlayer.TotalTime);

                // Update ListBox selection
                LstPlaylist.SelectedItem = item;

                // Start playing immediately
                _audioPlayer.Play();
            }
            catch (Exception ex)
            {
                OnAudioError($"{LocalizationService.PlaybackError}: {ex.Message}");
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_isUserSeeking) return;
            if ((DateTime.UtcNow - _lastSeekTime).TotalMilliseconds < 350) return;

            if (_audioPlayer.IsPlaying)
            {
                var cur = _audioPlayer.CurrentTime;
                var total = _audioPlayer.TotalTime;

                SliderSeek.Value = cur.TotalSeconds;
                TxtCurrentTime.Text = FormatTime(cur);
                TxtTotalTime.Text = FormatTime(total);

                if (total.TotalSeconds > 0 && SliderSeek.Maximum != total.TotalSeconds)
                {
                    SliderSeek.Maximum = total.TotalSeconds;
                }
            }
        }

        private void OnPlaybackStateChanged()
        {
            Dispatcher.Invoke(() =>
            {
                if (_audioPlayer.IsPlaying)
                {
                    PathPlay.Visibility = Visibility.Collapsed;
                    PathPause.Visibility = Visibility.Visible;
                }
                else
                {
                    PathPlay.Visibility = Visibility.Visible;
                    PathPause.Visibility = Visibility.Collapsed;
                }
                UpdatePlaylistDetailPlayPauseButton();
            });
        }

        private void OnTrackEnded()
        {
            Dispatcher.Invoke(() =>
            {
                var next = _playlistService.Next(isAutoAdvance: true);
                if (next != null)
                {
                    PlayTrack(next);
                }
                else
                {
                    _audioPlayer.Stop();
                    SliderSeek.Value = 0;
                    TxtCurrentTime.Text = FormatTime(TimeSpan.Zero);
                }
            });
        }

        private void OnCurrentTrackChanged(PlaylistItem? item)
        {
            Dispatcher.Invoke(() =>
            {
                if (item != null)
                {
                    PlayTrack(item);
                }
            });
        }

        private void OnPlaylistChanged()
        {
            Dispatcher.Invoke(() =>
            {
                LstPlaylist.Items.Refresh();
                UpdateCounts();
            });
        }

        private void OnSavedPlaylistsChanged()
        {
            Dispatcher.Invoke(() =>
            {
                LstSavedPlaylists.Items.Refresh();
                if (_selectedPlaylist != null)
                {
                    LstPlaylistSongs.Items.Refresh();
                }
                UpdateCounts();
            });
        }

        private void UpdateCounts()
        {
            if (TxtQueueCount != null) TxtQueueCount.Text = _playlistService.Count.ToString();
            if (TxtPlaylistsCount != null) TxtPlaylistsCount.Text = _savedPlaylistsService.Playlists.Count.ToString();
        }

        private void OnAudioError(string errorMessage)
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(this, errorMessage, LocalizationService.PlaybackError, MessageBoxButton.OK, MessageBoxImage.Warning);
            });
        }

        #region Window Controls & Header Drag-Restore

        private void Header_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindParent<ButtonBase>(e.OriginalSource as DependencyObject) != null)
            {
                return;
            }

            if (e.ClickCount == 2)
            {
                BtnMaximize_Click(sender, e);
                e.Handled = true;
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                _headerMouseDownPos = e.GetPosition(this);
                _isHeaderMouseDown = true;

                if (WindowState == WindowState.Normal)
                {
                    var helper = new WindowInteropHelper(this);
                    ReleaseCapture();
                    SendMessage(helper.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                    _isHeaderMouseDown = false;
                }
            }
        }

        private void Header_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isHeaderMouseDown && e.LeftButton == MouseButtonState.Pressed && WindowState == WindowState.Maximized)
            {
                Point currentPos = e.GetPosition(this);
                Vector diff = currentPos - _headerMouseDownPos;

                if (Math.Abs(diff.X) > 2 || Math.Abs(diff.Y) > 2)
                {
                    _isHeaderMouseDown = false;

                    GetCursorPos(out POINT cursorDevice);

                    Point cursorWpf = new Point(cursorDevice.X, cursorDevice.Y);
                    var source = PresentationSource.FromVisual(this);
                    if (source?.CompositionTarget != null)
                    {
                        cursorWpf = source.CompositionTarget.TransformFromDevice.Transform(cursorWpf);
                    }

                    double maxWidth = ActualWidth > 0 ? ActualWidth : SystemParameters.PrimaryScreenWidth;
                    double ratioX = _headerMouseDownPos.X / maxWidth;
                    ratioX = Math.Clamp(ratioX, 0.05, 0.95);

                    double normalWidth = _savedNormalWidth > 150 ? _savedNormalWidth : (RestoreBounds.Width > 150 ? RestoreBounds.Width : 500);
                    double normalHeight = _savedNormalHeight > 150 ? _savedNormalHeight : (RestoreBounds.Height > 150 ? RestoreBounds.Height : 640);

                    WindowState = WindowState.Normal;
                    Width = normalWidth;
                    Height = normalHeight;
                    Left = cursorWpf.X - (normalWidth * ratioX);
                    Top = Math.Max(0, cursorWpf.Y - 22);

                    UpdateLayout();

                    var helper = new WindowInteropHelper(this);
                    ReleaseCapture();
                    SendMessage(helper.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
                }
            }
        }

        private void Header_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isHeaderMouseDown = false;
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                Width = _savedNormalWidth > 150 ? _savedNormalWidth : 500;
                Height = _savedNormalHeight > 150 ? _savedNormalHeight : 640;
            }
            else
            {
                if (ActualWidth > 150) _savedNormalWidth = ActualWidth;
                if (ActualHeight > 150) _savedNormalHeight = ActualHeight;
                WindowState = WindowState.Maximized;
            }
        }

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                OuterBorder.Margin = new Thickness(0);
                OuterBorder.CornerRadius = new CornerRadius(0);
                PathMaximize.Data = Geometry.Parse("M 2 4 H 8 V 10 H 2 Z M 4 2 H 10 V 8 H 8");
                BtnMaximize.ToolTip = LocalizationService.Restore;
            }
            else
            {
                OuterBorder.Margin = new Thickness(0);
                OuterBorder.CornerRadius = new CornerRadius(18);
                PathMaximize.Data = Geometry.Parse("M 1 1 H 10 V 10 H 1 Z");
                BtnMaximize.ToolTip = LocalizationService.Maximize;
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region Playback Controls

        private void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_audioPlayer.CurrentFilePath == null && _playlistService.Count > 0)
            {
                if (_playlistService.CurrentItem == null)
                {
                    _playlistService.SetIndex(0);
                }
                else
                {
                    PlayTrack(_playlistService.CurrentItem);
                }
                return;
            }

            _audioPlayer.TogglePlayPause();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _audioPlayer.Stop();
            SliderSeek.Value = 0;
            TxtCurrentTime.Text = FormatTime(TimeSpan.Zero);
        }

        private void BtnPrevious_Click(object sender, RoutedEventArgs e)
        {
            if (_audioPlayer.CurrentTime.TotalSeconds > 3)
            {
                _audioPlayer.CurrentTime = TimeSpan.Zero;
                SliderSeek.Value = 0;
                TxtCurrentTime.Text = FormatTime(TimeSpan.Zero);
                return;
            }

            _playlistService.Previous();
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            _playlistService.Next();
        }

        private void QueueTrackSwitch(PlaylistItem item)
        {
            _pendingSkipItem = item;

            // Instantly update visual title and selection for snappy response
            TxtTitle.Text = item.Title;
            TxtArtist.Text = string.IsNullOrWhiteSpace(item.Artist) ? LocalizationService.UnknownArtist : item.Artist;
            LstPlaylist.SelectedItem = item;

            // Debounce actual audio engine loading to prevent crashes from rapid clicking
            _skipDebounceTimer.Stop();
            _skipDebounceTimer.Start();
        }

        private void SkipDebounceTimer_Tick(object? sender, EventArgs e)
        {
            _skipDebounceTimer.Stop();
            if (_pendingSkipItem != null)
            {
                var item = _pendingSkipItem;
                _pendingSkipItem = null;
                PlayTrack(item);
            }
        }

        private void BtnRepeat_Click(object sender, RoutedEventArgs e)
        {
            _playlistService.CycleRepeat();
            UpdateRepeatUi();
        }

        private void UpdateRepeatUi()
        {
            if (BtnRepeat == null || PathRepeat == null || BorderRepeatOneBadge == null) return;

            switch (_playlistService.Repeat)
            {
                case RepeatMode.Off:
                    PathRepeat.Stroke = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                    BorderRepeatOneBadge.Visibility = Visibility.Collapsed;
                    BtnRepeat.ToolTip = LocalizationService.RepeatOff;
                    BtnRepeat.Background = Brushes.Transparent;
                    break;
                case RepeatMode.All:
                    PathRepeat.Stroke = (Brush)FindResource("AccentBrush");
                    BorderRepeatOneBadge.Visibility = Visibility.Collapsed;
                    BtnRepeat.ToolTip = LocalizationService.RepeatAll;
                    BtnRepeat.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x06, 0xB6, 0xD4));
                    break;
                case RepeatMode.One:
                    PathRepeat.Stroke = (Brush)FindResource("AccentBrush");
                    BorderRepeatOneBadge.Visibility = Visibility.Visible;
                    BtnRepeat.ToolTip = LocalizationService.RepeatOne;
                    BtnRepeat.Background = new SolidColorBrush(Color.FromArgb(0x33, 0x06, 0xB6, 0xD4));
                    break;
            }
        }

        private void BtnVolumeMute_Click(object sender, RoutedEventArgs e)
        {
            if (_audioPlayer.Volume > 0)
            {
                _previousVolume = _audioPlayer.Volume;
                _audioPlayer.Volume = 0;
                SliderVolume.Value = 0;
            }
            else
            {
                float restore = _previousVolume > 0.05f ? _previousVolume : 0.5f;
                _audioPlayer.Volume = restore;
                SliderVolume.Value = restore;
            }
        }

        private void SliderVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audioPlayer == null) return;
            _audioPlayer.Volume = (float)e.NewValue;

            if (PathVolume != null)
            {
                if (e.NewValue <= 0.01)
                {
                    PathVolume.Data = Geometry.Parse("M 1 5 H 4 L 8 1 V 13 L 4 9 H 1 Z M 11 5 L 15 9 M 15 5 L 11 9");
                }
                else if (e.NewValue < 0.5)
                {
                    PathVolume.Data = Geometry.Parse("M 1 5 H 4 L 8 1 V 13 L 4 9 H 1 Z M 10 5.5 C 11.5 6.5 11.5 8.5 10 9.5");
                }
                else
                {
                    PathVolume.Data = Geometry.Parse("M 1 5 H 4 L 8 1 V 13 L 4 9 H 1 Z M 10 4 C 11.5 5.5 11.5 8.5 10 10 M 12 2 C 14.5 4.5 14.5 9.5 12 12");
                }
            }
        }

        private bool _isUserDraggingVolume = false;

        private void SliderVolume_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isUserDraggingVolume = true;
            SliderVolume.CaptureMouse();
            var pos = e.GetPosition(SliderVolume);
            UpdateVolumeFromMouse(pos);
            e.Handled = true;
        }

        private void SliderVolume_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(SliderVolume);
            UpdateVolumePreviewTooltip(pos);

            if (_isUserDraggingVolume && e.LeftButton == MouseButtonState.Pressed)
            {
                UpdateVolumeFromMouse(pos);
                e.Handled = true;
            }
        }

        private void SliderVolume_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isUserDraggingVolume)
            {
                var pos = e.GetPosition(SliderVolume);
                UpdateVolumeFromMouse(pos);
                SliderVolume.ReleaseMouseCapture();
                _isUserDraggingVolume = false;
                e.Handled = true;
            }
        }

        private void SliderVolume_MouseEnter(object sender, MouseEventArgs e)
        {
            UpdateVolumePreviewTooltip(e.GetPosition(SliderVolume));
        }

        private void SliderVolume_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isUserDraggingVolume)
            {
                UpdateVolumePreviewTooltip(e.GetPosition(SliderVolume));
            }
        }

        private void SliderVolume_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isUserDraggingVolume)
            {
                int currentPercent = (int)Math.Round(SliderVolume.Value * 100);
                if (SliderVolume.ToolTip is ToolTip tt)
                {
                    tt.Content = $"{LocalizationService.Volume}: {currentPercent}%";
                }
            }
        }

        private void UpdateVolumeFromMouse(Point mousePos)
        {
            if (SliderVolume.ActualWidth > 0)
            {
                const double snapThreshold = 10.0;
                double ratio;
                if (mousePos.X <= snapThreshold)
                {
                    ratio = 0.0;
                }
                else if (mousePos.X >= SliderVolume.ActualWidth - snapThreshold)
                {
                    ratio = 1.0;
                }
                else
                {
                    ratio = (mousePos.X - snapThreshold) / (SliderVolume.ActualWidth - (2 * snapThreshold));
                    ratio = Math.Clamp(ratio, 0.0, 1.0);
                }

                SliderVolume.Value = ratio;
                _audioPlayer.Volume = (float)ratio;
            }
        }

        private void UpdateVolumePreviewTooltip(Point mousePos)
        {
            if (SliderVolume.ActualWidth <= 0) return;
            const double snapThreshold = 10.0;
            double ratio;
            if (mousePos.X <= snapThreshold)
            {
                ratio = 0.0;
            }
            else if (mousePos.X >= SliderVolume.ActualWidth - snapThreshold)
            {
                ratio = 1.0;
            }
            else
            {
                ratio = (mousePos.X - snapThreshold) / (SliderVolume.ActualWidth - (2 * snapThreshold));
                ratio = Math.Clamp(ratio, 0.0, 1.0);
            }
            int targetPercent = (int)Math.Round(ratio * 100);

            if (SliderVolume.ToolTip is ToolTip tt)
            {
                tt.Content = $"{LocalizationService.Volume}: {targetPercent}%";
            }
            else
            {
                SliderVolume.ToolTip = new ToolTip { Content = $"{LocalizationService.Volume}: {targetPercent}%" };
            }
        }

        #endregion

        #region Headphone & Global Media Key Controls

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_APPCOMMAND = 0x0319;
        private const int WM_HOTKEY = 0x0312;

        private const int VK_VOLUME_MUTE = 0xAD;
        private const int VK_VOLUME_DOWN = 0xAE;
        private const int VK_VOLUME_UP = 0xAF;
        private const int VK_MEDIA_NEXT_TRACK = 0xB0;
        private const int VK_MEDIA_PREV_TRACK = 0xB1;
        private const int VK_MEDIA_STOP = 0xB2;
        private const int VK_MEDIA_PLAY_PAUSE = 0xB3;

        private const int APPCOMMAND_VOLUME_MUTE = 8;
        private const int APPCOMMAND_VOLUME_DOWN = 9;
        private const int APPCOMMAND_VOLUME_UP = 10;
        private const int APPCOMMAND_MEDIA_NEXTTRACK = 11;
        private const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;
        private const int APPCOMMAND_MEDIA_STOP = 13;
        private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
        private const int APPCOMMAND_MEDIA_PLAY = 46;
        private const int APPCOMMAND_MEDIA_PAUSE = 47;

        private const uint MOD_NOREPEAT = 0x4000;
        private const int HOTKEY_PLAY_PAUSE = 9001;
        private const int HOTKEY_NEXT_TRACK = 9002;
        private const int HOTKEY_PREV_TRACK = 9003;
        private const int HOTKEY_STOP = 9004;

        private static readonly UIntPtr SELF_INJECTED_EXTRA_INFO = (UIntPtr)0x564C5854; // 'VLXT'

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private IntPtr _hookId = IntPtr.Zero;
        private LowLevelKeyboardProc? _hookProc;

        private DateTime _lastVolUpTime = DateTime.MinValue;
        private float? _volBeforeUp;
        private bool _consumeNextVolUpKeyUp = false;

        private DateTime _lastVolDownTime = DateTime.MinValue;
        private float? _volBeforeDown;
        private bool _consumeNextVolDownKeyUp = false;

        private DateTime _lastMediaPlayPauseTime = DateTime.MinValue;
        private DateTime _lastMediaNextTime = DateTime.MinValue;
        private DateTime _lastMediaPrevTime = DateTime.MinValue;

        private static MMDeviceEnumerator? _deviceEnumerator;
        private static MMDevice? _cachedRenderDevice;
        private static readonly object _volumeLock = new();

        private static float? GetSystemMasterVolume()
        {
            lock (_volumeLock)
            {
                try
                {
                    _deviceEnumerator ??= new MMDeviceEnumerator();
                    _cachedRenderDevice ??= _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    return _cachedRenderDevice?.AudioEndpointVolume?.MasterVolumeLevelScalar;
                }
                catch
                {
                    try
                    {
                        _cachedRenderDevice = null;
                        _deviceEnumerator = new MMDeviceEnumerator();
                        _cachedRenderDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        return _cachedRenderDevice?.AudioEndpointVolume?.MasterVolumeLevelScalar;
                    }
                    catch { return null; }
                }
            }
        }

        private static void SetSystemMasterVolume(float level)
        {
            lock (_volumeLock)
            {
                try
                {
                    _deviceEnumerator ??= new MMDeviceEnumerator();
                    _cachedRenderDevice ??= _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (_cachedRenderDevice?.AudioEndpointVolume != null)
                    {
                        _cachedRenderDevice.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f);
                    }
                }
                catch
                {
                    try
                    {
                        _cachedRenderDevice = null;
                        _deviceEnumerator = new MMDeviceEnumerator();
                        _cachedRenderDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        if (_cachedRenderDevice?.AudioEndpointVolume != null)
                        {
                            _cachedRenderDevice.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(level, 0f, 1f);
                        }
                    }
                    catch { }
                }
            }
        }

        private void RegisterGlobalHotKeys(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                RegisterHotKey(hwnd, HOTKEY_PLAY_PAUSE, MOD_NOREPEAT, (uint)VK_MEDIA_PLAY_PAUSE);
                RegisterHotKey(hwnd, HOTKEY_NEXT_TRACK, MOD_NOREPEAT, (uint)VK_MEDIA_NEXT_TRACK);
                RegisterHotKey(hwnd, HOTKEY_PREV_TRACK, MOD_NOREPEAT, (uint)VK_MEDIA_PREV_TRACK);
                RegisterHotKey(hwnd, HOTKEY_STOP, MOD_NOREPEAT, (uint)VK_MEDIA_STOP);
            }
            catch { }
        }

        private void UnregisterGlobalHotKeys(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                UnregisterHotKey(hwnd, HOTKEY_PLAY_PAUSE);
                UnregisterHotKey(hwnd, HOTKEY_NEXT_TRACK);
                UnregisterHotKey(hwnd, HOTKEY_PREV_TRACK);
                UnregisterHotKey(hwnd, HOTKEY_STOP);
            }
            catch { }
        }

        private void InitializeHeadphoneControls()
        {
            try
            {
                if (_hookId != IntPtr.Zero) return;
                _hookProc = HookCallback;
                IntPtr hMod = IntPtr.Zero;
                try
                {
                    using var curProcess = Process.GetCurrentProcess();
                    using var curModule = curProcess.MainModule;
                    if (curModule != null)
                    {
                        hMod = GetModuleHandle(curModule.ModuleName);
                    }
                }
                catch { }

                if (hMod == IntPtr.Zero)
                {
                    hMod = GetModuleHandle(null);
                }

                _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, hMod, 0);
            }
            catch
            {
            }
        }

        private void CleanupHeadphoneControls()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                int vkCode = Marshal.ReadInt32(lParam);

                // Offset 16 is dwExtraInfo in KBDLLHOOKSTRUCT (both 64-bit and 32-bit aligned)
                UIntPtr extraInfo = (UIntPtr)Marshal.ReadIntPtr(lParam, 16);
                bool isSelfInjected = (extraInfo == SELF_INJECTED_EXTRA_INFO);

                // Only ignore keys that we synthesized ourselves to prevent feedback loops;
                // Allow all headphone, Bluetooth and driver-injected key events through!
                if (!isSelfInjected)
                {
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        if (vkCode == VK_MEDIA_PLAY_PAUSE)
                        {
                            HandlePlayPauseCommand();
                            return (IntPtr)1;
                        }
                        else if (vkCode == VK_MEDIA_NEXT_TRACK)
                        {
                            HandleNextTrackCommand();
                            return (IntPtr)1;
                        }
                        else if (vkCode == VK_MEDIA_PREV_TRACK)
                        {
                            HandlePrevTrackCommand();
                            return (IntPtr)1;
                        }
                        else if (vkCode == VK_MEDIA_STOP)
                        {
                            HandleStopCommand();
                            return (IntPtr)1;
                        }
                        else if (vkCode == VK_VOLUME_UP)
                        {
                            DateTime now = DateTime.UtcNow;
                            if ((now - _lastVolUpTime).TotalMilliseconds <= 550)
                            {
                                // Rapid double tap of Volume Up -> switch to Next file, restore volume
                                _lastVolUpTime = DateTime.MinValue;
                                _consumeNextVolUpKeyUp = true;

                                if (_volBeforeUp.HasValue)
                                {
                                    float targetVol = _volBeforeUp.Value;
                                    Task.Run(() => SetSystemMasterVolume(targetVol));
                                }
                                else
                                {
                                    keybd_event((byte)VK_VOLUME_DOWN, 0, 0, SELF_INJECTED_EXTRA_INFO);
                                    keybd_event((byte)VK_VOLUME_DOWN, 0, 2, SELF_INJECTED_EXTRA_INFO);
                                }

                                HandleNextTrackCommand();
                                return (IntPtr)1;
                            }
                            else
                            {
                                // First press: record current system volume, let Windows adjust volume normally
                                _volBeforeUp = GetSystemMasterVolume();
                                _lastVolUpTime = now;
                            }
                        }
                        else if (vkCode == VK_VOLUME_DOWN)
                        {
                            DateTime now = DateTime.UtcNow;
                            if ((now - _lastVolDownTime).TotalMilliseconds <= 550)
                            {
                                // Rapid double tap of Volume Down -> switch to Previous file, restore volume
                                _lastVolDownTime = DateTime.MinValue;
                                _consumeNextVolDownKeyUp = true;

                                if (_volBeforeDown.HasValue)
                                {
                                    float targetVol = _volBeforeDown.Value;
                                    Task.Run(() => SetSystemMasterVolume(targetVol));
                                }
                                else
                                {
                                    keybd_event((byte)VK_VOLUME_UP, 0, 0, SELF_INJECTED_EXTRA_INFO);
                                    keybd_event((byte)VK_VOLUME_UP, 0, 2, SELF_INJECTED_EXTRA_INFO);
                                }

                                HandlePrevTrackCommand();
                                return (IntPtr)1;
                            }
                            else
                            {
                                // First press: record current system volume, let Windows adjust volume normally
                                _volBeforeDown = GetSystemMasterVolume();
                                _lastVolDownTime = now;
                            }
                        }
                    }
                    else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                    {
                        if (vkCode == VK_VOLUME_UP && _consumeNextVolUpKeyUp)
                        {
                            _consumeNextVolUpKeyUp = false;
                            return (IntPtr)1;
                        }
                        if (vkCode == VK_VOLUME_DOWN && _consumeNextVolDownKeyUp)
                        {
                            _consumeNextVolDownKeyUp = false;
                            return (IntPtr)1;
                        }
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private void HandlePlayPauseCommand()
        {
            if ((DateTime.UtcNow - _lastMediaPlayPauseTime).TotalMilliseconds < 250) return;
            _lastMediaPlayPauseTime = DateTime.UtcNow;

            Dispatcher.BeginInvoke(() =>
            {
                BtnPlayPause_Click(this, new RoutedEventArgs());
            });
        }

        private void HandleNextTrackCommand()
        {
            if ((DateTime.UtcNow - _lastMediaNextTime).TotalMilliseconds < 250) return;
            _lastMediaNextTime = DateTime.UtcNow;

            Dispatcher.BeginInvoke(() =>
            {
                BtnNext_Click(this, new RoutedEventArgs());
            });
        }

        private void HandlePrevTrackCommand()
        {
            if ((DateTime.UtcNow - _lastMediaPrevTime).TotalMilliseconds < 250) return;
            _lastMediaPrevTime = DateTime.UtcNow;

            Dispatcher.BeginInvoke(() =>
            {
                BtnPrevious_Click(this, new RoutedEventArgs());
            });
        }

        private void HandleStopCommand()
        {
            Dispatcher.BeginInvoke(() =>
            {
                BtnStop_Click(this, new RoutedEventArgs());
            });
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_PLAY_PAUSE)
                {
                    HandlePlayPauseCommand();
                    handled = true;
                    return (IntPtr)1;
                }
                else if (id == HOTKEY_NEXT_TRACK)
                {
                    HandleNextTrackCommand();
                    handled = true;
                    return (IntPtr)1;
                }
                else if (id == HOTKEY_PREV_TRACK)
                {
                    HandlePrevTrackCommand();
                    handled = true;
                    return (IntPtr)1;
                }
                else if (id == HOTKEY_STOP)
                {
                    HandleStopCommand();
                    handled = true;
                    return (IntPtr)1;
                }
            }
            else if (msg == WM_APPCOMMAND)
            {
                int cmd = (int)(((uint)lParam >> 16) & ~0xF000);
                if (cmd == APPCOMMAND_MEDIA_PLAY_PAUSE || cmd == APPCOMMAND_MEDIA_PLAY || cmd == APPCOMMAND_MEDIA_PAUSE)
                {
                    HandlePlayPauseCommand();
                    handled = true;
                    return (IntPtr)1;
                }
                else if (cmd == APPCOMMAND_MEDIA_NEXTTRACK)
                {
                    HandleNextTrackCommand();
                    handled = true;
                    return (IntPtr)1;
                }
                else if (cmd == APPCOMMAND_MEDIA_PREVIOUSTRACK)
                {
                    HandlePrevTrackCommand();
                    handled = true;
                    return (IntPtr)1;
                }
                else if (cmd == APPCOMMAND_MEDIA_STOP)
                {
                    HandleStopCommand();
                    handled = true;
                    return (IntPtr)1;
                }
            }
            return IntPtr.Zero;
        }

        #endregion

        #region In-App Notifications & Modal Confirmation Dialogs

        public void ShowToast(string message)
        {
            ToastText.Text = message;
            ToastBorder.Opacity = 1;
            _toastTimer?.Stop();
            _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            _toastTimer.Tick += (s, e) =>
            {
                _toastTimer.Stop();
                var anim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
                ToastBorder.BeginAnimation(UIElement.OpacityProperty, anim);
            };
            _toastTimer.Start();
        }

        public void ShowConfirmModal(string title, string message, Action onConfirm, string confirmButtonText = "Удалить")
        {
            TxtModalTitle.Text = title;
            TxtModalMessage.Text = message;
            BtnModalConfirm.Content = confirmButtonText;
            _modalConfirmAction = onConfirm;
            ModalConfirmOverlay.Visibility = Visibility.Visible;
        }

        private void BtnModalCancel_Click(object sender, RoutedEventArgs e)
        {
            ModalConfirmOverlay.Visibility = Visibility.Collapsed;
            _modalConfirmAction = null;
        }

        private void BtnModalConfirm_Click(object sender, RoutedEventArgs e)
        {
            ModalConfirmOverlay.Visibility = Visibility.Collapsed;
            var action = _modalConfirmAction;
            _modalConfirmAction = null;
            action?.Invoke();
        }

        #endregion

        #region Drawer Navigation & Tabs

        private void BtnDrawerClose_Click(object sender, RoutedEventArgs e)
        {
            PlaylistDrawer.Visibility = Visibility.Collapsed;
        }

        private void BtnPlaylistToggle_Click(object sender, RoutedEventArgs e)
        {
            if (PlaylistDrawer.Visibility == Visibility.Visible && TabRadioQueue.IsChecked == true)
            {
                PlaylistDrawer.Visibility = Visibility.Collapsed;
            }
            else
            {
                PlaylistDrawer.Visibility = Visibility.Visible;
                ShowQueueTab();
            }
        }

        private void BtnPlaylistsToggle_Click(object sender, RoutedEventArgs e)
        {
            if (PlaylistDrawer.Visibility == Visibility.Visible && TabRadioPlaylists.IsChecked == true)
            {
                PlaylistDrawer.Visibility = Visibility.Collapsed;
            }
            else
            {
                PlaylistDrawer.Visibility = Visibility.Visible;
                ShowPlaylistsTab();
            }
        }

        private void TabRadioQueue_Click(object sender, RoutedEventArgs e)
        {
            ShowQueueTab();
        }

        private void TabRadioQueue_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ShowQueueTab();
            e.Handled = true;
        }

        private void TabRadioPlaylists_Click(object sender, RoutedEventArgs e)
        {
            ShowPlaylistsTab();
        }

        private void TabRadioPlaylists_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ShowPlaylistsTab();
            e.Handled = true;
        }

        private void ShowQueueTab()
        {
            TabRadioQueue.IsChecked = true;
            TabRadioPlaylists.IsChecked = false;
            GridQueueView.Visibility = Visibility.Visible;
            GridPlaylistsView.Visibility = Visibility.Collapsed;
        }

        private void ShowPlaylistsTab()
        {
            TabRadioQueue.IsChecked = false;
            TabRadioPlaylists.IsChecked = true;
            GridQueueView.Visibility = Visibility.Collapsed;
            GridPlaylistsView.Visibility = Visibility.Visible;
            PanelCreatePlaylist.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Queue Operations (Reorder with Insertion Line, Delete, Shuffle, Save as Playlist)

        private void LstPlaylist_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstPlaylist.SelectedItem is PlaylistItem item)
            {
                int index = _playlistService.Items.IndexOf(item);
                if (index >= 0)
                {
                    if (index == _playlistService.CurrentIndex && _audioPlayer.HasTrack)
                    {
                        if (_audioPlayer.IsPlaying)
                            _audioPlayer.Pause();
                        else
                            _audioPlayer.Play();
                    }
                    else
                    {
                        _playlistService.SetIndex(index);
                    }
                }
            }
        }

        private void LstPlaylist_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindParent<Button>(e.OriginalSource as DependencyObject) != null) return;
            _queueDragStart = e.GetPosition(null);
            var item = FindParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            _draggedQueueItem = item?.DataContext as PlaylistItem;
            _isQueueItemClicked = _draggedQueueItem != null;
        }

        private void LstPlaylist_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedQueueItem != null && _isQueueItemClicked)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _queueDragStart - mousePos;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isQueueItemClicked = false;
                    var itemToDrag = _draggedQueueItem;
                    _draggedQueueItem = null;
                    var data = new DataObject("QueueItem", itemToDrag);
                    DragDrop.DoDragDrop(LstPlaylist, data, DragDropEffects.Move);
                    ClearDropIndicators(LstPlaylist);
                }
            }
        }

        private void LstPlaylist_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Single-click to switch song immediately or toggle play/pause if already active
            if (_isQueueItemClicked && _draggedQueueItem != null)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _queueDragStart - mousePos;
                if (Math.Abs(diff.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(diff.Y) <= SystemParameters.MinimumVerticalDragDistance)
                {
                    int index = _playlistService.Items.IndexOf(_draggedQueueItem);
                    if (index >= 0)
                    {
                        if (index == _playlistService.CurrentIndex && _audioPlayer.HasTrack)
                        {
                            if (_audioPlayer.IsPlaying)
                                _audioPlayer.Pause();
                            else
                                _audioPlayer.Play();
                        }
                        else
                        {
                            _playlistService.SetIndex(index);
                        }
                    }
                }
            }
            _isQueueItemClicked = false;
            _draggedQueueItem = null;
        }

        private void BtnDuplicateQueueItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PlaylistItem item)
            {
                _playlistService.AddItem(item.FilePath);
                ShowToast(LocalizationService.AddedToQueueToast);
            }
        }

        private void BtnDeleteQueueItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PlaylistItem item)
            {
                _playlistService.RemoveItem(item);
            }
        }

        private void BtnShuffleQueue_Click(object sender, RoutedEventArgs e)
        {
            _playlistService.ShuffleQueue();
        }

        private void BtnSaveQueueAsPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (_playlistService.Count == 0)
            {
                ShowToast(LocalizationService.QueueEmptySave);
                return;
            }

            string defaultName = $"{LocalizationService.TabQueue} ({DateTime.Now:dd.MM HH:mm})";
            var pl = _savedPlaylistsService.CreateFromQueue(defaultName, _playlistService.Items);
            OpenPlaylistDetail(pl);
        }

        private void BtnAddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = LocalizationService.BtnAddFiles,
                Filter = "Audio files|*.mp3;*.m4a;*.aac;*.mp4;*.flac;*.wav;*.ogg;*.opus|All files (*.*)|*.*",
                Multiselect = true
            };

            if (dlg.ShowDialog(this) == true)
            {
                _playlistService.AddFiles(dlg.FileNames, autoPlay: false);
            }
        }

        private void BtnSavePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (_playlistService.Count == 0)
            {
                ShowToast(LocalizationService.QueueEmptyExport);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = LocalizationService.BtnExportQueue,
                Filter = "M3U8 Playlist (*.m3u8)|*.m3u8|M3U Playlist (*.m3u)|*.m3u",
                FileName = "queue_playlist.m3u8"
            };

            if (dlg.ShowDialog(this) == true)
            {
                bool ok = _playlistService.SavePlaylist(dlg.FileName);
                if (ok)
                {
                    ShowToast(LocalizationService.ExportSuccess);
                }
            }
        }

        private void BtnClearPlaylist_Click(object sender, RoutedEventArgs e)
        {
            _playlistService.Clear();
            _audioPlayer.Stop();
            TxtTitle.Text = LocalizationService.NoFile;
            TxtArtist.Text = LocalizationService.OpenFileHint;
            SliderSeek.Value = 0;
            TxtCurrentTime.Text = FormatTime(TimeSpan.Zero);
            TxtTotalTime.Text = FormatTime(TimeSpan.Zero);
            RenderOptions.SetBitmapScalingMode(ImgCoverArt, BitmapScalingMode.NearestNeighbor);
            ImgCoverArt.Source = ArtworkService.GetDefaultCoverArt();
        }

        #endregion

        #region Saved Playlists Operations (Create, Detail, Inline Rename, Reorder, Delete, Export, Import)

        private void BtnShowCreatePlaylist_Click(object sender, RoutedEventArgs e)
        {
            PanelCreatePlaylist.Visibility = Visibility.Visible;
            TxtNewPlaylistName.Text = $"{LocalizationService.T("Плейлист", "Playlist")} {_savedPlaylistsService.Playlists.Count + 1}";
            TxtNewPlaylistName.Focus();
            TxtNewPlaylistName.SelectAll();
        }

        private void BtnConfirmCreatePlaylist_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtNewPlaylistName.Text.Trim();
            if (!string.IsNullOrEmpty(name))
            {
                var pl = _savedPlaylistsService.CreatePlaylist(name);
                PanelCreatePlaylist.Visibility = Visibility.Collapsed;
                OpenPlaylistDetail(pl);
            }
        }

        private void BtnCancelCreatePlaylist_Click(object sender, RoutedEventArgs e)
        {
            PanelCreatePlaylist.Visibility = Visibility.Collapsed;
        }

        private void TxtNewPlaylistName_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnConfirmCreatePlaylist_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                BtnCancelCreatePlaylist_Click(sender, e);
                e.Handled = true;
            }
        }

        private void BtnPlaySavedPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LocalPlaylist pl)
            {
                PlaySavedPlaylist(pl);
            }
        }

        private void BtnShuffleSavedPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LocalPlaylist pl)
            {
                ShuffleAndPlayPlaylist(pl);
            }
        }

        private void BtnDeleteSavedPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is LocalPlaylist pl)
            {
                ShowConfirmModal(LocalizationService.DeleteTitle, string.Format(LocalizationService.DeletePlaylistPrompt, pl.Name), () =>
                {
                    _savedPlaylistsService.DeletePlaylist(pl);
                });
            }
        }

        private void LstSavedPlaylists_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (LstSavedPlaylists.SelectedItem is LocalPlaylist pl)
            {
                OpenPlaylistDetail(pl);
            }
        }

        private void OpenPlaylistDetail(LocalPlaylist pl)
        {
            _selectedPlaylist = pl;
            TxtCurrentPlaylistTitle.Text = pl.Name;
            LstPlaylistSongs.ItemsSource = pl.Songs;
            ViewPlaylistsMaster.Visibility = Visibility.Collapsed;
            ViewPlaylistDetail.Visibility = Visibility.Visible;
            CancelRenameCurrentPlaylist();
            UpdatePlaylistDetailPlayPauseButton();
            ShowPlaylistsTab();
        }

        private void BtnBackToPlaylists_Click(object sender, RoutedEventArgs e)
        {
            CancelRenameCurrentPlaylist();
            _selectedPlaylist = null;
            ViewPlaylistDetail.Visibility = Visibility.Collapsed;
            ViewPlaylistsMaster.Visibility = Visibility.Visible;
        }

        // --- Playlist Inline Renaming ---
        private void PanelPlaylistTitleView_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            StartRenameCurrentPlaylist();
        }

        private void StartRenameCurrentPlaylist()
        {
            if (_selectedPlaylist == null) return;
            PanelPlaylistTitleView.Visibility = Visibility.Collapsed;
            TxtCurrentPlaylistTitleEdit.Visibility = Visibility.Visible;
            TxtCurrentPlaylistTitleEdit.Text = _selectedPlaylist.Name;
            TxtCurrentPlaylistTitleEdit.Focus();
            TxtCurrentPlaylistTitleEdit.SelectAll();
        }

        private void TxtCurrentPlaylistTitleEdit_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitRenameCurrentPlaylist();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                CancelRenameCurrentPlaylist();
                e.Handled = true;
            }
        }

        private void TxtCurrentPlaylistTitleEdit_LostFocus(object sender, RoutedEventArgs e)
        {
            CommitRenameCurrentPlaylist();
        }

        private void CommitRenameCurrentPlaylist()
        {
            if (_selectedPlaylist != null && TxtCurrentPlaylistTitleEdit.Visibility == Visibility.Visible)
            {
                string newName = TxtCurrentPlaylistTitleEdit.Text.Trim();
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    _savedPlaylistsService.RenamePlaylist(_selectedPlaylist, newName);
                    TxtCurrentPlaylistTitle.Text = _selectedPlaylist.Name;
                    LstSavedPlaylists.Items.Refresh();
                }
            }
            TxtCurrentPlaylistTitleEdit.Visibility = Visibility.Collapsed;
            PanelPlaylistTitleView.Visibility = Visibility.Visible;
        }

        private void CancelRenameCurrentPlaylist()
        {
            if (TxtCurrentPlaylistTitleEdit != null)
            {
                TxtCurrentPlaylistTitleEdit.Visibility = Visibility.Collapsed;
            }
            if (PanelPlaylistTitleView != null)
            {
                PanelPlaylistTitleView.Visibility = Visibility.Visible;
            }
        }

        private void PlaySavedPlaylist(LocalPlaylist pl, int startIndex = 0)
        {
            if (pl.Songs.Count == 0)
            {
                ShowToast(LocalizationService.PlaylistEmpty);
                return;
            }

            _playlistService.LoadFromLocalPlaylist(pl, startIndex);
            UpdatePlaylistDetailPlayPauseButton();
        }

        private void BtnPlayCurrentPlaylistAll_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlaylist == null) return;

            if (_audioPlayer.IsPlaying)
            {
                _audioPlayer.Pause();
            }
            else if (_audioPlayer.IsPaused && _playlistService.Count > 0)
            {
                _audioPlayer.Play();
            }
            else
            {
                PlaySavedPlaylist(_selectedPlaylist, 0);
            }
            UpdatePlaylistDetailPlayPauseButton();
        }

        private void BtnShufflePlaylistOrder_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlaylist != null)
            {
                _savedPlaylistsService.ShufflePlaylist(_selectedPlaylist);
                LstPlaylistSongs.Items.Refresh();
            }
        }

        private void ShuffleAndPlayPlaylist(LocalPlaylist pl)
        {
            if (pl.Songs.Count == 0)
            {
                ShowToast(LocalizationService.PlaylistEmpty);
                return;
            }

            _playlistService.LoadFromLocalPlaylist(pl, 0);
            _playlistService.ShuffleQueue();
            if (_playlistService.CurrentItem != null)
            {
                PlayTrack(_playlistService.CurrentItem);
            }
            UpdatePlaylistDetailPlayPauseButton();
        }

        private void UpdatePlaylistDetailPlayPauseButton()
        {
            if (BtnPlayCurrentPlaylistAll == null) return;
            BtnPlayCurrentPlaylistAll.Content = _audioPlayer.IsPlaying ? LocalizationService.BtnPause : LocalizationService.BtnPlay;
        }

        private void BtnAddCurrentPlaylistToQueue_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlaylist != null && _selectedPlaylist.Songs.Count > 0)
            {
                var paths = _selectedPlaylist.Songs.Select(s => s.FilePath);
                _playlistService.AddFiles(paths, autoPlay: false);
                ShowToast(string.Format(LocalizationService.AddedToQueueCount, _selectedPlaylist.Songs.Count));
            }
        }

        private void BtnDeleteSongFromPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlaylist != null && sender is Button btn && btn.Tag is PlaylistSong song)
            {
                _savedPlaylistsService.RemoveSong(_selectedPlaylist, song);
            }
        }

        private void BtnAddTracksToCurrentPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlaylist == null) return;

            var dlg = new OpenFileDialog
            {
                Title = $"{LocalizationService.BtnAddFiles} ({_selectedPlaylist.Name})",
                Filter = "Audio files|*.mp3;*.m4a;*.aac;*.mp4;*.flac;*.wav;*.ogg;*.opus|All files (*.*)|*.*",
                Multiselect = true
            };

            if (dlg.ShowDialog(this) == true)
            {
                _savedPlaylistsService.AddSongs(_selectedPlaylist, dlg.FileNames);
            }
        }

        private void BtnExportCurrentPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlaylist == null || _selectedPlaylist.Songs.Count == 0)
            {
                ShowToast(LocalizationService.QueueEmptyExport);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Title = LocalizationService.BtnExportPlaylist,
                Filter = "M3U8 Playlist (*.m3u8)|*.m3u8|M3U Playlist (*.m3u)|*.m3u",
                FileName = $"{_selectedPlaylist.Name}.m3u8"
            };

            if (dlg.ShowDialog(this) == true)
            {
                bool ok = _savedPlaylistsService.ExportToM3u8(_selectedPlaylist, dlg.FileName);
                if (ok)
                {
                    ShowToast(LocalizationService.ExportSuccess);
                }
            }
        }

        private void BtnDeleteCurrentPlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPlaylist == null) return;

            var toDelete = _selectedPlaylist;
            ShowConfirmModal(LocalizationService.DeleteTitle, string.Format(LocalizationService.DeletePlaylistPrompt, toDelete.Name), () =>
            {
                BtnBackToPlaylists_Click(sender, e);
                _savedPlaylistsService.DeletePlaylist(toDelete);
            });
        }

        private void LstPlaylistSongs_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_selectedPlaylist != null && LstPlaylistSongs.SelectedItem is PlaylistSong song)
            {
                int index = _selectedPlaylist.Songs.IndexOf(song);
                if (index >= 0)
                {
                    if (_audioPlayer.HasTrack && string.Equals(_audioPlayer.CurrentFilePath, song.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        if (_audioPlayer.IsPlaying)
                            _audioPlayer.Pause();
                        else
                            _audioPlayer.Play();
                    }
                    else
                    {
                        PlaySavedPlaylist(_selectedPlaylist, index);
                    }
                }
            }
        }

        private void LstPlaylistSongs_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (FindParent<Button>(e.OriginalSource as DependencyObject) != null) return;
            _playlistSongDragStart = e.GetPosition(null);
            var item = FindParent<ListBoxItem>(e.OriginalSource as DependencyObject);
            _draggedPlaylistSong = item?.DataContext as PlaylistSong;
            _isPlaylistSongClicked = _draggedPlaylistSong != null;
        }

        private void LstPlaylistSongs_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedPlaylistSong != null && _isPlaylistSongClicked)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _playlistSongDragStart - mousePos;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isPlaylistSongClicked = false;
                    var songToDrag = _draggedPlaylistSong;
                    _draggedPlaylistSong = null;
                    var data = new DataObject("PlaylistItemSong", songToDrag);
                    DragDrop.DoDragDrop(LstPlaylistSongs, data, DragDropEffects.Move);
                    ClearDropIndicators(LstPlaylistSongs);
                }
            }
        }

        private void LstPlaylistSongs_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Single-click to play song immediately or toggle play/pause if already active
            if (_isPlaylistSongClicked && _draggedPlaylistSong != null && _selectedPlaylist != null)
            {
                Point mousePos = e.GetPosition(null);
                Vector diff = _playlistSongDragStart - mousePos;
                if (Math.Abs(diff.X) <= SystemParameters.MinimumHorizontalDragDistance &&
                    Math.Abs(diff.Y) <= SystemParameters.MinimumVerticalDragDistance)
                {
                    int index = _selectedPlaylist.Songs.IndexOf(_draggedPlaylistSong);
                    if (index >= 0)
                    {
                        if (_audioPlayer.HasTrack && string.Equals(_audioPlayer.CurrentFilePath, _draggedPlaylistSong.FilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            if (_audioPlayer.IsPlaying)
                                _audioPlayer.Pause();
                            else
                                _audioPlayer.Play();
                        }
                        else
                        {
                            PlaySavedPlaylist(_selectedPlaylist, index);
                        }
                    }
                }
            }
            _isPlaylistSongClicked = false;
            _draggedPlaylistSong = null;
        }

        private void BtnAddSongToQueue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is PlaylistSong song)
            {
                _playlistService.AddItem(song.FilePath);
                ShowToast(LocalizationService.AddedToQueueToast);
            }
        }

        private void LstPlaylistSongs_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("PlaylistItemSong"))
            {
                e.Effects = DragDropEffects.Move;
                var targetContainer = FindParent<ListBoxItem>(e.OriginalSource as DependencyObject);
                if (targetContainer != null)
                {
                    Point pos = e.GetPosition(targetContainer);
                    bool isUpper = pos.Y < targetContainer.ActualHeight / 2.0;
                    UpdateDropIndicator(LstPlaylistSongs, targetContainer, isUpper);
                    int idx = LstPlaylistSongs.ItemContainerGenerator.IndexFromContainer(targetContainer);
                    _playlistSongReorderTargetIndex = isUpper ? idx : idx + 1;
                }
                else
                {
                    ClearDropIndicators(LstPlaylistSongs);
                    _playlistSongReorderTargetIndex = _selectedPlaylist?.Songs.Count ?? 0;
                }
                e.Handled = true;
                return;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void LstPlaylistSongs_DragLeave(object sender, DragEventArgs e)
        {
            ClearDropIndicators(LstPlaylistSongs);
        }

        private void LstPlaylistSongs_Drop(object sender, DragEventArgs e)
        {
            ClearDropIndicators(LstPlaylistSongs);

            if (_selectedPlaylist == null) return;

            if (e.Data.GetDataPresent("PlaylistItemSong"))
            {
                var sourceSong = e.Data.GetData("PlaylistItemSong") as PlaylistSong;
                if (sourceSong != null)
                {
                    int oldIdx = _selectedPlaylist.Songs.IndexOf(sourceSong);
                    int newIdx = _playlistSongReorderTargetIndex;
                    if (oldIdx >= 0 && newIdx >= 0)
                    {
                        if (newIdx > oldIdx) newIdx--;
                        newIdx = Math.Clamp(newIdx, 0, _selectedPlaylist.Songs.Count - 1);
                        if (oldIdx != newIdx)
                        {
                            _savedPlaylistsService.MoveSong(_selectedPlaylist, oldIdx, newIdx);
                        }
                    }
                }
                e.Handled = true;
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    var audioFiles = ExtractAudioFiles(files);
                    if (audioFiles.Count > 0)
                    {
                        _savedPlaylistsService.AddSongs(_selectedPlaylist, audioFiles);
                    }
                }
                e.Handled = true;
            }
        }

        private void BtnImportPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = LocalizationService.BtnImportM3U8,
                Filter = "Playlists (*.m3u;*.m3u8)|*.m3u;*.m3u8|All files (*.*)|*.*"
            };

            if (dlg.ShowDialog(this) == true)
            {
                var pl = _savedPlaylistsService.ImportFromM3u(dlg.FileName);
                if (pl != null)
                {
                    OpenPlaylistDetail(pl);
                }
                else
                {
                    MessageBox.Show(this, LocalizationService.ImportFailed, LocalizationService.ImportTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BtnOpenFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = LocalizationService.OpenFile,
                Filter = "All supported files|*.mp3;*.m4a;*.aac;*.mp4;*.flac;*.wav;*.ogg;*.opus;*.m3u;*.m3u8|" +
                         "Audio files (*.mp3;*.flac;*.wav;...)|*.mp3;*.m4a;*.aac;*.mp4;*.flac;*.wav;*.ogg;*.opus|" +
                         "Playlists (*.m3u;*.m3u8)|*.m3u;*.m3u8|" +
                         "All files (*.*)|*.*",
                Multiselect = true
            };

            if (dlg.ShowDialog(this) == true)
            {
                if (dlg.FileNames.Length == 1)
                {
                    string singlePath = dlg.FileName;
                    if (PlaylistService.IsPlaylistFile(singlePath))
                    {
                        _playlistService.LoadPlaylist(singlePath);
                    }
                    else
                    {
                        _playlistService.PlaySingleFile(singlePath);
                    }

                    if (_playlistService.CurrentItem != null)
                    {
                        PlayTrack(_playlistService.CurrentItem);
                    }
                }
                else
                {
                    _playlistService.Clear();
                    _playlistService.AddFiles(dlg.FileNames);
                    if (_playlistService.CurrentItem != null)
                    {
                        PlayTrack(_playlistService.CurrentItem);
                    }
                }
            }
        }

        #endregion

        #region Insertion Indicator Helpers ("чёрточка между файлами")

        private static void UpdateDropIndicator(ListBox listBox, ListBoxItem targetItem, bool isUpper)
        {
            for (int i = 0; i < listBox.Items.Count; i++)
            {
                if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem container)
                {
                    var top = FindVisualChild<Border>(container, "TopDropIndicator");
                    var bottom = FindVisualChild<Border>(container, "BottomDropIndicator");
                    if (container == targetItem)
                    {
                        if (top != null) top.Visibility = isUpper ? Visibility.Visible : Visibility.Collapsed;
                        if (bottom != null) bottom.Visibility = isUpper ? Visibility.Collapsed : Visibility.Visible;
                    }
                    else
                    {
                        if (top != null) top.Visibility = Visibility.Collapsed;
                        if (bottom != null) bottom.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        private static void ClearDropIndicators(ListBox listBox)
        {
            for (int i = 0; i < listBox.Items.Count; i++)
            {
                if (listBox.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem container)
                {
                    var top = FindVisualChild<Border>(container, "TopDropIndicator");
                    var bottom = FindVisualChild<Border>(container, "BottomDropIndicator");
                    if (top != null) top.Visibility = Visibility.Collapsed;
                    if (bottom != null) bottom.Visibility = Visibility.Collapsed;
                }
            }
        }

        #endregion

        #region Seeking: Click-to-Jump, Drag & Hover Preview

        private void SliderSeek_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_audioPlayer.TotalTime.TotalSeconds <= 0) return;

            _lastSeekHoverPos = e.GetPosition(SliderSeek);
            _isSeekHoverActive = true;
            _seekHoverTimer.Stop();
            _seekHoverTimer.Start();
        }

        private void SliderSeek_MouseMove(object sender, MouseEventArgs e)
        {
            if (_audioPlayer.TotalTime.TotalSeconds <= 0)
            {
                HideSeekPreview();
                return;
            }

            _lastSeekHoverPos = e.GetPosition(SliderSeek);

            if (BadgeSeekPreview.Visibility == Visibility.Visible)
            {
                UpdateSeekPreview(_lastSeekHoverPos);
            }
            else if (_isSeekHoverActive && !_seekHoverTimer.IsEnabled)
            {
                _seekHoverTimer.Start();
            }
        }

        private void SliderSeek_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isUserSeeking)
            {
                HideSeekPreview();
            }
        }

        private void SeekHoverTimer_Tick(object? sender, EventArgs e)
        {
            _seekHoverTimer.Stop();

            if (_isSeekHoverActive && _audioPlayer.TotalTime.TotalSeconds > 0 && SliderSeek.IsMouseOver)
            {
                UpdateSeekPreview(_lastSeekHoverPos);
                BadgeSeekPreview.Visibility = Visibility.Visible;
            }
        }

        private void UpdateSeekPreview(Point mousePos)
        {
            if (SliderSeek.ActualWidth <= 0 || _audioPlayer.TotalTime.TotalSeconds <= 0)
            {
                return;
            }

            const double snapThreshold = 16.0;
            double ratio;
            if (mousePos.X <= snapThreshold)
            {
                ratio = 0.0;
            }
            else if (mousePos.X >= SliderSeek.ActualWidth - snapThreshold)
            {
                ratio = 1.0;
            }
            else
            {
                ratio = (mousePos.X - snapThreshold) / (SliderSeek.ActualWidth - (2 * snapThreshold));
                ratio = Math.Clamp(ratio, 0.0, 1.0);
            }

            double targetSeconds = SliderSeek.Minimum + (SliderSeek.Maximum - SliderSeek.Minimum) * ratio;
            var previewTime = TimeSpan.FromSeconds(targetSeconds);

            TxtSeekPreview.Text = FormatTime(previewTime);

            BadgeSeekPreview.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double badgeWidth = BadgeSeekPreview.DesiredSize.Width > 0 ? BadgeSeekPreview.DesiredSize.Width : 48;
            double left = Math.Clamp(mousePos.X - (badgeWidth / 2.0), 0, Math.Max(0, SliderSeek.ActualWidth - badgeWidth));
            Canvas.SetLeft(BadgeSeekPreview, left);
        }

        private void HideSeekPreview()
        {
            _isSeekHoverActive = false;
            _seekHoverTimer.Stop();
            if (BadgeSeekPreview != null)
            {
                BadgeSeekPreview.Visibility = Visibility.Collapsed;
            }
        }

        private void SliderSeek_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isUserSeeking = true;
            var slider = (Slider)sender;
            slider.CaptureMouse();
            var pos = e.GetPosition(slider);
            UpdateSeekFromMouse(slider, pos, applyAudioSeek: false);
            UpdateSeekPreview(pos);
            BadgeSeekPreview.Visibility = Visibility.Visible;
        }

        private void SliderSeek_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_isUserSeeking && e.LeftButton == MouseButtonState.Pressed)
            {
                var slider = (Slider)sender;
                var pos = e.GetPosition(slider);
                UpdateSeekFromMouse(slider, pos, applyAudioSeek: false);
                UpdateSeekPreview(pos);
            }
        }

        private void SliderSeek_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isUserSeeking)
            {
                var slider = (Slider)sender;
                var pos = e.GetPosition(slider);
                UpdateSeekFromMouse(slider, pos, applyAudioSeek: true);
                slider.ReleaseMouseCapture();
                _isUserSeeking = false;
                _lastSeekTime = DateTime.UtcNow;

                if (!slider.IsMouseOver)
                {
                    HideSeekPreview();
                }
            }
        }

        private void UpdateSeekFromMouse(Slider slider, Point mousePos, bool applyAudioSeek)
        {
            if (slider.ActualWidth > 0)
            {
                const double snapThreshold = 16.0;
                double ratio;
                if (mousePos.X <= snapThreshold)
                {
                    ratio = 0.0;
                }
                else if (mousePos.X >= slider.ActualWidth - snapThreshold)
                {
                    ratio = 1.0;
                }
                else
                {
                    ratio = (mousePos.X - snapThreshold) / (slider.ActualWidth - (2 * snapThreshold));
                    ratio = Math.Clamp(ratio, 0.0, 1.0);
                }

                double targetSeconds = slider.Minimum + (slider.Maximum - slider.Minimum) * ratio;
                slider.Value = targetSeconds;
                TxtCurrentTime.Text = FormatTime(TimeSpan.FromSeconds(targetSeconds));
                if (applyAudioSeek)
                {
                    _audioPlayer.CurrentTime = TimeSpan.FromSeconds(targetSeconds);
                    _lastSeekTime = DateTime.UtcNow;
                }
            }
        }

        #endregion

        #region Drag and Drop & Keyboard

        private List<string> ExtractAudioFiles(string[] files)
        {
            var audioFiles = new List<string>();
            foreach (var f in files)
            {
                if (File.Exists(f) && PlaylistService.IsSupportedFile(f))
                {
                    audioFiles.Add(f);
                }
                else if (Directory.Exists(f))
                {
                    try
                    {
                        var dirFiles = Directory.GetFiles(f, "*.*", SearchOption.AllDirectories)
                            .Where(PlaylistService.IsSupportedFile);
                        audioFiles.AddRange(dirFiles);
                    }
                    catch
                    {
                    }
                }
            }
            return audioFiles;
        }

        private void PlaylistDrawer_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropCueQueue.Visibility = Visibility.Visible;
            }
            else if (e.Data.GetDataPresent("QueueItem") || e.Data.GetDataPresent("PlaylistItemSong"))
            {
                e.Effects = DragDropEffects.Move;
            }
            e.Handled = true;
        }

        private void PlaylistDrawer_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("QueueItem"))
            {
                e.Effects = DragDropEffects.Move;
                var targetContainer = FindParent<ListBoxItem>(e.OriginalSource as DependencyObject);
                if (targetContainer != null)
                {
                    Point pos = e.GetPosition(targetContainer);
                    bool isUpper = pos.Y < targetContainer.ActualHeight / 2.0;
                    UpdateDropIndicator(LstPlaylist, targetContainer, isUpper);
                    int idx = LstPlaylist.ItemContainerGenerator.IndexFromContainer(targetContainer);
                    _queueReorderTargetIndex = isUpper ? idx : idx + 1;
                }
                else
                {
                    ClearDropIndicators(LstPlaylist);
                    _queueReorderTargetIndex = _playlistService.Items.Count;
                }
                e.Handled = true;
                return;
            }
            else if (e.Data.GetDataPresent("PlaylistItemSong"))
            {
                e.Effects = DragDropEffects.Move;
                var targetContainer = FindParent<ListBoxItem>(e.OriginalSource as DependencyObject);
                if (targetContainer != null)
                {
                    Point pos = e.GetPosition(targetContainer);
                    bool isUpper = pos.Y < targetContainer.ActualHeight / 2.0;
                    UpdateDropIndicator(LstPlaylistSongs, targetContainer, isUpper);
                    int idx = LstPlaylistSongs.ItemContainerGenerator.IndexFromContainer(targetContainer);
                    _playlistSongReorderTargetIndex = isUpper ? idx : idx + 1;
                }
                else
                {
                    ClearDropIndicators(LstPlaylistSongs);
                    _playlistSongReorderTargetIndex = _selectedPlaylist?.Songs.Count ?? 0;
                }
                e.Handled = true;
                return;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropCueQueue.Visibility = Visibility.Visible;
                e.Handled = true;
                return;
            }

            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void PlaylistDrawer_DragLeave(object sender, DragEventArgs e)
        {
            DropCueQueue.Visibility = Visibility.Collapsed;
            ClearDropIndicators(LstPlaylist);
            ClearDropIndicators(LstPlaylistSongs);
        }

        private void PlaylistDrawer_Drop(object sender, DragEventArgs e)
        {
            DropCueQueue.Visibility = Visibility.Collapsed;
            ClearDropIndicators(LstPlaylist);
            ClearDropIndicators(LstPlaylistSongs);

            // Internal Queue Reorder
            if (e.Data.GetDataPresent("QueueItem"))
            {
                var sourceItem = e.Data.GetData("QueueItem") as PlaylistItem;
                if (sourceItem != null)
                {
                    int oldIndex = _playlistService.Items.IndexOf(sourceItem);
                    int newIndex = _queueReorderTargetIndex;
                    if (oldIndex >= 0 && newIndex >= 0)
                    {
                        if (newIndex > oldIndex) newIndex--;
                        newIndex = Math.Clamp(newIndex, 0, _playlistService.Items.Count - 1);
                        if (oldIndex != newIndex)
                        {
                            _playlistService.MoveItem(oldIndex, newIndex);
                        }
                    }
                }
                e.Handled = true;
                return;
            }

            // Internal Playlist Song Reorder
            if (e.Data.GetDataPresent("PlaylistItemSong") && _selectedPlaylist != null)
            {
                var sourceSong = e.Data.GetData("PlaylistItemSong") as PlaylistSong;
                if (sourceSong != null)
                {
                    int oldIdx = _selectedPlaylist.Songs.IndexOf(sourceSong);
                    int newIdx = _playlistSongReorderTargetIndex;
                    if (oldIdx >= 0 && newIdx >= 0)
                    {
                        if (newIdx > oldIdx) newIdx--;
                        newIdx = Math.Clamp(newIdx, 0, _selectedPlaylist.Songs.Count - 1);
                        if (oldIdx != newIdx)
                        {
                            _savedPlaylistsService.MoveSong(_selectedPlaylist, oldIdx, newIdx);
                        }
                    }
                }
                e.Handled = true;
                return;
            }

            // External File Drop: add to queue without launching!
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;

            var audioFiles = ExtractAudioFiles(files);
            if (audioFiles.Count > 0)
            {
                _playlistService.AddFiles(audioFiles, autoPlay: false);
            }
            e.Handled = true;
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (files == null || files.Length == 0) return;

            var playlistFile = files.FirstOrDefault(PlaylistService.IsPlaylistFile);
            if (!string.IsNullOrEmpty(playlistFile))
            {
                _playlistService.LoadPlaylist(playlistFile);
                return;
            }

            var audioFiles = ExtractAudioFiles(files);
            if (audioFiles.Count == 0) return;

            // If queue drawer is visible: always add to queue without launching!
            if (PlaylistDrawer.Visibility == Visibility.Visible)
            {
                _playlistService.AddFiles(audioFiles, autoPlay: false);
                return;
            }

            // If queue drawer is closed:
            // Dragging a file into the player window REPLACES the song playing right now!
            _playlistService.PlaySingleFile(audioFiles[0]);
            if (audioFiles.Count > 1)
            {
                _playlistService.AddFiles(audioFiles.Skip(1), autoPlay: false);
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape && PlaylistDrawer.Visibility == Visibility.Visible)
            {
                PlaylistDrawer.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Space)
            {
                BtnPlayPause_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Left)
            {
                _audioPlayer.CurrentTime -= TimeSpan.FromSeconds(5);
                _lastSeekTime = DateTime.UtcNow;
                e.Handled = true;
            }
            else if (e.Key == Key.Right)
            {
                _audioPlayer.CurrentTime += TimeSpan.FromSeconds(5);
                _lastSeekTime = DateTime.UtcNow;
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                SliderVolume.Value = Math.Min(1.0, SliderVolume.Value + 0.05);
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                SliderVolume.Value = Math.Max(0.0, SliderVolume.Value - 0.05);
                e.Handled = true;
            }
            else if (e.Key == Key.M)
            {
                BtnVolumeMute_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.O && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                BtnOpenFile_Click(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                if (PlaylistDrawer.Visibility == Visibility.Visible)
                {
                    PlaylistDrawer.Visibility = Visibility.Collapsed;
                    e.Handled = true;
                }
            }
        }

        #endregion

        private static string FormatTime(TimeSpan time)
        {
            if (time.TotalHours >= 1)
            {
                return time.ToString(@"h\:mm\:ss");
            }
            return time.ToString(@"mm\:ss");
        }
    }
}
