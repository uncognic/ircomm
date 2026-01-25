using System;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

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

                var link = new Hyperlink(new Run(urlText))
                {
                    NavigateUri = new Uri(normalized, UriKind.Absolute),
                    ToolTip = normalized
                };

                link.Click += (s, ev) =>
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(link.NavigateUri.AbsoluteUri) { UseShellExecute = true });
                    }
                    catch (Exception)
                    {
                        Debug.WriteLine("Failed to open link: " + link.NavigateUri.AbsoluteUri);
                    }
                };

                tb.Inlines.Add(link);

                lastIndex = m.Index + m.Length;
            }

            if (lastIndex < text.Length)
            {
                tb.Inlines.Add(new Run(text.Substring(lastIndex)));
            }
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

            return "http://" + url;
        }
    }
}