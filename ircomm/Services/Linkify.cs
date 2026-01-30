using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ircomm.Services
{
    public static class Linkify
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.RegisterAttached(
                "Text",
                typeof(string),
                typeof(Linkify),
                new PropertyMetadata(string.Empty, OnTextChanged));
        public static void SetText(DependencyObject element, string value) =>
            element.SetValue(TextProperty, value);

        public static string GetText(DependencyObject element) =>
            (string)element.GetValue(TextProperty);

        private static readonly Regex UrlRegex = new Regex(
            @"(?<url>(https?://[^\s]+|www\.[^\s]+))",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly string[] ImageExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp"};

        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;

            tb.Inlines.Clear();

            var text = e.NewValue as string ?? string.Empty;
            if (string.IsNullOrEmpty(text)) return;

            var lastIndex = 0;
            foreach (Match m in UrlRegex.Matches(text))
            {
                if (m.Index > lastIndex)
                {
                    var segment = text.Substring(lastIndex, m.Index - lastIndex);
                    tb.Inlines.Add(new Run(segment));
                }

                var urlText = m.Groups["url"].Value;
                var normalized = NormalizeUrl(urlText);
                normalized = TrimTrailingPunctuation(normalized);

    
                var textLink = new Hyperlink(new Run(urlText))
                {
                    ToolTip = normalized
                };
                if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
                    textLink.NavigateUri = uri;
                else
                    Debug.WriteLine($"Linkify: invalid URI '{normalized}'");

                textLink.Click += (s, ev) =>
                {
                    try
                    {
                        if (textLink.NavigateUri != null)
                            Process.Start(new ProcessStartInfo(textLink.NavigateUri.AbsoluteUri) { UseShellExecute = true });
                    }
                    catch { }
                };

                tb.Inlines.Add(textLink);

     
                if (IsImageUrl(normalized))
                {
          
                    tb.Inlines.Add(new Run(" "));

                    var image = new Image
                    {
                        MaxWidth = 480,
                        MaxHeight = 320,
                        Stretch = Stretch.Uniform,
                        Margin = new Thickness(4)
                    };

                    var container = new InlineUIContainer(image);
                    tb.Inlines.Add(container);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var bytes = await _httpClient.GetByteArrayAsync(normalized).ConfigureAwait(false);

                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                try
                                {
                                    using var ms = new MemoryStream(bytes);
                                    var bmp = new BitmapImage();
                                    bmp.BeginInit();
                                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                                    bmp.StreamSource = ms;
                                    bmp.EndInit();
                                    bmp.Freeze();

                                    image.Source = bmp;
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Linkify: image failed for '{normalized}': {ex.Message}");
                                    if (tb.Inlines.Contains(container))
                                        tb.Inlines.Remove(container);
                                }
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Linkify: download failed for '{normalized}': {ex.Message}");
                            Application.Current?.Dispatcher.Invoke(() =>
                            {
                                if (tb.Inlines.Contains(container))
                                    tb.Inlines.Remove(container);
                            });
                        }
                    });
                }

                lastIndex = m.Index + m.Length;
            }

            if (lastIndex < text.Length)
            {
                tb.Inlines.Add(new Run(text.Substring(lastIndex)));
            }
        }

        private static bool IsImageUrl(string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
                var ext = Path.GetExtension(uri.AbsolutePath);
                if (string.IsNullOrEmpty(ext)) return false;
                foreach (var e in ImageExtensions)
                {
                    if (ext.Equals(e, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static string NormalizeUrl(string url)
        {
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            if (url.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
            {
                return "http://" + url;
            }

            if (url.StartsWith("//"))
                return "http:" + url;

            return "http://" + url;
        }

        private static string TrimTrailingPunctuation(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;

            while (url.Length > 0 && ",.;:!)\"'".IndexOf(url[^1]) >= 0)
            {
                url = url[..^1];
            }

            if (url.StartsWith("<") && url.EndsWith(">"))
                url = url.Substring(1, url.Length - 2);
            return url;
        }
    }
}