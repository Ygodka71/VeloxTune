using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ModernPlayer.Services
{
    public class TrackMetadata
    {
        public string Title { get; set; } = "Нет файла";
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;
        public ImageSource? CoverArt { get; set; }
        public bool HasEmbeddedCover { get; set; }
    }

    public static class ArtworkService
    {
        private static ImageSource? _cachedDefaultCover;

        public static ImageSource GetDefaultCoverArt()
        {
            if (_cachedDefaultCover != null) return _cachedDefaultCover;

            // 1. Try to load from BaseDirectory/img/icon.png or CurrentDirectory/img/icon.png
            string[] possiblePaths = 
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "img", "icon.png"),
                Path.Combine(Directory.GetCurrentDirectory(), "img", "icon.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "img", "icon.png")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        bmp.Freeze();
                        _cachedDefaultCover = bmp;
                        return bmp;
                    }
                    catch
                    {
                    }
                }
            }

            // 2. Try WPF pack URI resource
            try
            {
                var uri = new Uri("pack://application:,,,/img/icon.png", UriKind.RelativeOrAbsolute);
                var bmp = new BitmapImage(uri);
                bmp.Freeze();
                _cachedDefaultCover = bmp;
                return bmp;
            }
            catch
            {
            }

            // 3. Fallback to procedural art
            _cachedDefaultCover = GenerateCoverArt("Modern Player", "Audio");
            return _cachedDefaultCover;
        }

        public static TrackMetadata ExtractMetadata(string filePath)
        {
            var meta = new TrackMetadata();
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
            meta.Title = fileNameWithoutExt;

            try
            {
                using var file = TagLib.File.Create(filePath);
                if (!string.IsNullOrWhiteSpace(file.Tag.Title))
                {
                    meta.Title = file.Tag.Title.Trim();
                }

                if (!string.IsNullOrWhiteSpace(file.Tag.FirstPerformer))
                {
                    meta.Artist = file.Tag.FirstPerformer.Trim();
                }
                else if (!string.IsNullOrWhiteSpace(file.Tag.FirstAlbumArtist))
                {
                    meta.Artist = file.Tag.FirstAlbumArtist.Trim();
                }

                if (!string.IsNullOrWhiteSpace(file.Tag.Album))
                {
                    meta.Album = file.Tag.Album.Trim();
                }

                meta.Duration = file.Properties.Duration;

                if (file.Tag.Pictures != null && file.Tag.Pictures.Length > 0)
                {
                    var pic = file.Tag.Pictures[0];
                    if (pic.Data?.Data != null && pic.Data.Data.Length > 0)
                    {
                        var image = LoadImageFromBytes(pic.Data.Data);
                        if (image != null)
                        {
                            meta.CoverArt = image;
                            meta.HasEmbeddedCover = true;
                        }
                    }
                }
            }
            catch
            {
                // Fallback to filename
            }

            // If no embedded cover art found, use icon from img/icon.png!
            if (meta.CoverArt == null)
            {
                meta.CoverArt = GetDefaultCoverArt();
                meta.HasEmbeddedCover = false;
            }

            return meta;
        }

        private static BitmapImage? LoadImageFromBytes(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                var image = new BitmapImage();
                image.BeginInit();
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = ms;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                return null;
            }
        }

        public static BitmapSource GenerateCoverArt(string title, string artist)
        {
            int width = 512;
            int height = 512;

            string seedStr = $"{title}_{artist}".ToLowerInvariant();
            uint hash = 2166136261;
            foreach (char c in seedStr)
            {
                hash = (hash ^ c) * 16777619;
            }

            var palettes = new[]
            {
                (Color.FromRgb(15, 23, 42), Color.FromRgb(59, 130, 246), Color.FromRgb(6, 182, 212)),
                (Color.FromRgb(30, 27, 75), Color.FromRgb(139, 92, 246), Color.FromRgb(236, 72, 153)),
                (Color.FromRgb(6, 78, 59), Color.FromRgb(16, 185, 129), Color.FromRgb(52, 211, 153)),
                (Color.FromRgb(28, 25, 23), Color.FromRgb(245, 158, 11), Color.FromRgb(239, 68, 68)),
                (Color.FromRgb(11, 19, 43), Color.FromRgb(79, 70, 229), Color.FromRgb(56, 189, 248)),
                (Color.FromRgb(24, 24, 27), Color.FromRgb(244, 63, 94), Color.FromRgb(251, 113, 133))
            };

            var palette = palettes[hash % palettes.Length];

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var bgBrush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1)
                };
                bgBrush.GradientStops.Add(new GradientStop(palette.Item1, 0.0));
                bgBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 
                    (byte)((palette.Item1.R + palette.Item2.R) / 2),
                    (byte)((palette.Item1.G + palette.Item2.G) / 2),
                    (byte)((palette.Item1.B + palette.Item2.B) / 2)), 0.45));
                bgBrush.GradientStops.Add(new GradientStop(palette.Item2, 1.0));

                dc.DrawRectangle(bgBrush, null, new Rect(0, 0, width, height));

                double cardSize = 240;
                var cardRect = new Rect((width - cardSize) / 2.0, (height - cardSize) / 2.0, cardSize, cardSize);
                var cardBg = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
                var cardBorder = new Pen(new LinearGradientBrush(
                    Color.FromArgb(110, 255, 255, 255),
                    Color.FromArgb(30, 255, 255, 255),
                    45.0), 1.8);
                cardBorder.Freeze();
                dc.DrawRoundedRectangle(cardBg, cardBorder, cardRect, 36, 36);

                var noteBrush = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1)
                };
                noteBrush.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0.0));
                noteBrush.GradientStops.Add(new GradientStop(Color.FromArgb(240, palette.Item3.R, palette.Item3.G, palette.Item3.B), 1.0));
                noteBrush.Freeze();

                double cx = width / 2.0;
                double cy = height / 2.0 - 15;

                dc.DrawEllipse(noteBrush, null, new Point(cx - 32, cy + 30), 24, 18);
                dc.DrawEllipse(noteBrush, null, new Point(cx + 32, cy + 14), 24, 18);

                var stemPen = new Pen(noteBrush, 7.0)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                stemPen.Freeze();

                dc.DrawLine(stemPen, new Point(cx - 14, cy + 28), new Point(cx - 14, cy - 42));
                dc.DrawLine(stemPen, new Point(cx + 50, cy + 12), new Point(cx + 50, cy - 58));

                var beamPen = new Pen(noteBrush, 12.0)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                beamPen.Freeze();
                dc.DrawLine(beamPen, new Point(cx - 15, cy - 40), new Point(cx + 51, cy - 56));
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            return rtb;
        }
    }
}
