using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Input;

namespace AudioSrtPlayer;

public partial class MainWindow : Window
{
    private static readonly Regex TimingLineRegex = new(
        @"(?<start>\d{1,2}:\d{2}:\d{2}[\.,]\d{3})\s*-->\s*(?<end>\d{1,2}:\d{2}:\d{2}[\.,]\d{3})",
        RegexOptions.Compiled);

    public ObservableCollection<SubtitleEntry> Subtitles { get; } = [];

    private readonly DispatcherTimer _syncTimer;
    private bool _isUserSeeking;
    private bool _isPlaying;
    private string? _audioFilePath;
    private readonly List<int> _searchMatches = [];
    private int _searchMatchPointer = -1;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _syncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150)
        };
        _syncTimer.Tick += SyncTimer_Tick;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
    }

    private void OpenAudioButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select audio file",
            Filter = "Audio Files|*.mp3;*.wav;*.m4a;*.aac;*.wma;*.flac;*.ogg|All Files|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _audioFilePath = dialog.FileName;
        AudioPathText.Text = _audioFilePath;

        AudioElement.Stop();
        AudioElement.Source = new Uri(_audioFilePath);
        _isPlaying = false;
        PositionSlider.Value = 0;
        CurrentTimeText.Text = "00:00";
    }

    private void OpenSrtButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select subtitle file",
            Filter = "SRT Files|*.srt|All Files|*.*"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var parsedSubtitles = ParseSrt(File.ReadAllText(dialog.FileName));
            Subtitles.Clear();

            foreach (var subtitle in parsedSubtitles)
            {
                Subtitles.Add(subtitle);
            }

            SrtPathText.Text = dialog.FileName;
            SubtitleList.SelectedIndex = Subtitles.Count > 0 ? 0 : -1;
            UpdateCurrentSubtitleStatus();
            RefreshSearchMatches();

            if (Subtitles.Count == 0)
            {
                MessageBox.Show(this, "No valid subtitle entries were found in this SRT file.", "SRT Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Unable to parse SRT file.\n\n{ex.Message}", "SRT Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_audioFilePath))
        {
            MessageBox.Show(this, "Select an audio file first.", "Audio Required", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        AudioElement.Play();
        _syncTimer.Start();
        _isPlaying = true;
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        AudioElement.Pause();
        _isPlaying = false;
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        AudioElement.Stop();
        _syncTimer.Stop();
        _isPlaying = false;
        PositionSlider.Value = 0;
        CurrentTimeText.Text = "00:00";
        if (Subtitles.Count > 0)
        {
            SubtitleList.SelectedIndex = 0;
            SubtitleList.ScrollIntoView(Subtitles[0]);
        }

        UpdateCurrentSubtitleStatus();
    }

    private void AudioElement_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (AudioElement.NaturalDuration.HasTimeSpan)
        {
            PositionSlider.Maximum = AudioElement.NaturalDuration.TimeSpan.TotalSeconds;
            DurationText.Text = FormatTime(AudioElement.NaturalDuration.TimeSpan);
        }
    }

    private void AudioElement_MediaEnded(object sender, RoutedEventArgs e)
    {
        _syncTimer.Stop();
        _isPlaying = false;
        PositionSlider.Value = 0;
        CurrentTimeText.Text = "00:00";
    }

    private void AudioElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _syncTimer.Stop();
        _isPlaying = false;
        MessageBox.Show(this, $"Audio playback failed.\n\n{e.ErrorException?.Message}", "Playback Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            if (string.IsNullOrWhiteSpace(_audioFilePath))
            {
                return;
            }

            if (_isPlaying)
            {
                AudioElement.Pause();
                _isPlaying = false;
            }
            else
            {
                AudioElement.Play();
                _syncTimer.Start();
                _isPlaying = true;
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Left)
        {
            SeekBy(TimeSpan.FromSeconds(-5));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Right)
        {
            SeekBy(TimeSpan.FromSeconds(5));
            e.Handled = true;
        }
    }

    private void PositionSlider_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isUserSeeking = true;
    }

    private void PositionSlider_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (AudioElement.Source is not null)
        {
            AudioElement.Position = TimeSpan.FromSeconds(PositionSlider.Value);
            HighlightCurrentSubtitle(AudioElement.Position);
        }

        _isUserSeeking = false;
    }

    private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        RefreshSearchMatches();
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            GoToNextSearchMatch();
            e.Handled = true;
        }
    }

    private void SearchNextButton_Click(object sender, RoutedEventArgs e)
    {
        GoToNextSearchMatch();
    }

    private void SearchPreviousButton_Click(object sender, RoutedEventArgs e)
    {
        GoToPreviousSearchMatch();
    }

    private void SyncTimer_Tick(object? sender, EventArgs e)
    {
        if (AudioElement.Source is null)
        {
            return;
        }

        var currentPosition = AudioElement.Position;

        if (!_isUserSeeking)
        {
            PositionSlider.Value = currentPosition.TotalSeconds;
        }

        CurrentTimeText.Text = FormatTime(currentPosition);
        HighlightCurrentSubtitle(currentPosition);
    }

    private void HighlightCurrentSubtitle(TimeSpan position)
    {
        if (Subtitles.Count == 0)
        {
            SubtitleList.SelectedIndex = -1;
            return;
        }

        var index = -1;
        var latestStartedIndex = -1;
        for (var i = 0; i < Subtitles.Count; i++)
        {
            if (Subtitles[i].Start <= position)
            {
                latestStartedIndex = i;
            }

            if (position >= Subtitles[i].Start && position <= Subtitles[i].End)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            index = latestStartedIndex;
        }

        if (SubtitleList.SelectedIndex != index)
        {
            SubtitleList.SelectedIndex = index;
            if (index >= 0)
            {
                SubtitleList.ScrollIntoView(Subtitles[index]);
            }
        }

        UpdateCurrentSubtitleStatus();
    }

    private void SeekBy(TimeSpan delta)
    {
        if (AudioElement.Source is null)
        {
            return;
        }

        var current = AudioElement.Position;
        var target = current + delta;

        if (target < TimeSpan.Zero)
        {
            target = TimeSpan.Zero;
        }

        if (AudioElement.NaturalDuration.HasTimeSpan && target > AudioElement.NaturalDuration.TimeSpan)
        {
            target = AudioElement.NaturalDuration.TimeSpan;
        }

        AudioElement.Position = target;
        PositionSlider.Value = target.TotalSeconds;
        CurrentTimeText.Text = FormatTime(target);
        HighlightCurrentSubtitle(target);
    }

    private void RefreshSearchMatches()
    {
        _searchMatches.Clear();
        _searchMatchPointer = -1;

        var query = SearchTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchStatusText.Text = "No active search.";
            return;
        }

        for (var i = 0; i < Subtitles.Count; i++)
        {
            if (Subtitles[i].Text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                _searchMatches.Add(i);
            }
        }

        if (_searchMatches.Count == 0)
        {
            SearchStatusText.Text = "No matches found.";
            return;
        }

        SearchStatusText.Text = $"{_searchMatches.Count} matches found.";
    }

    private void GoToNextSearchMatch()
    {
        if (_searchMatches.Count == 0)
        {
            return;
        }

        _searchMatchPointer = (_searchMatchPointer + 1) % _searchMatches.Count;
        JumpToSearchMatch();
    }

    private void GoToPreviousSearchMatch()
    {
        if (_searchMatches.Count == 0)
        {
            return;
        }

        _searchMatchPointer = (_searchMatchPointer - 1 + _searchMatches.Count) % _searchMatches.Count;
        JumpToSearchMatch();
    }

    private void JumpToSearchMatch()
    {
        if (_searchMatchPointer < 0 || _searchMatchPointer >= _searchMatches.Count)
        {
            return;
        }

        var subtitleIndex = _searchMatches[_searchMatchPointer];
        SubtitleList.SelectedIndex = subtitleIndex;
        SubtitleList.ScrollIntoView(Subtitles[subtitleIndex]);
        SearchStatusText.Text = $"Match {_searchMatchPointer + 1} of {_searchMatches.Count}.";

        if (AudioElement.Source is not null)
        {
            var start = Subtitles[subtitleIndex].Start;
            AudioElement.Position = start;
            PositionSlider.Value = start.TotalSeconds;
            CurrentTimeText.Text = FormatTime(start);
            HighlightCurrentSubtitle(start);
        }
        else
        {
            UpdateCurrentSubtitleStatus();
        }
    }

    private void UpdateCurrentSubtitleStatus()
    {
        var index = SubtitleList.SelectedIndex;
        if (index < 0 || index >= Subtitles.Count)
        {
            CurrentSubtitleStatusText.Text = "Current subtitle: none";
            return;
        }

        var subtitle = Subtitles[index];
        var current = subtitle.Text.Replace(Environment.NewLine, " ");
        CurrentSubtitleStatusText.Text = $"Current subtitle: [{subtitle.TimeRangeDisplay}] {current}";
    }

    private static List<SubtitleEntry> ParseSrt(string srtContent)
    {
        var subtitles = new List<SubtitleEntry>();
        var normalizedContent = srtContent
            .Replace("\uFEFF", string.Empty)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Trim();

        if (string.IsNullOrWhiteSpace(normalizedContent))
        {
            return subtitles;
        }

        var blocks = normalizedContent.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        foreach (var block in blocks)
        {
            var lines = block
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .ToArray();

            if (lines.Length < 2)
            {
                continue;
            }

            var timingLineIndex = Array.FindIndex(lines, line => line.Contains("-->", StringComparison.Ordinal));
            if (timingLineIndex < 0)
            {
                continue;
            }

            var match = TimingLineRegex.Match(lines[timingLineIndex]);
            if (!match.Success)
            {
                continue;
            }

            if (!TryParseTimestamp(match.Groups["start"].Value, out var start) ||
                !TryParseTimestamp(match.Groups["end"].Value, out var end))
            {
                continue;
            }

            var textLines = lines.Skip(timingLineIndex + 1).ToArray();
            if (textLines.Length == 0)
            {
                continue;
            }

            subtitles.Add(new SubtitleEntry(start, end, string.Join(Environment.NewLine, textLines)));
        }

        return subtitles;
    }

    private static bool TryParseTimestamp(string value, out TimeSpan result)
    {
        var sanitized = value.Trim().Replace('.', ',');
        var formats = new[]
        {
            "h\\:mm\\:ss\\,fff",
            "hh\\:mm\\:ss\\,fff"
        };

        return TimeSpan.TryParseExact(sanitized, formats, CultureInfo.InvariantCulture, out result);
    }

    private static string FormatTime(TimeSpan value)
    {
        return value.TotalHours >= 1 ? value.ToString("hh\\:mm\\:ss") : value.ToString("mm\\:ss");
    }
}

public sealed record SubtitleEntry(TimeSpan Start, TimeSpan End, string Text)
{
    public string TimeRangeDisplay => $"{FormatTimestamp(Start)} - {FormatTimestamp(End)}";
    public string DisplayText => $"[{TimeRangeDisplay}] {Text}";

    private static string FormatTimestamp(TimeSpan value)
    {
        return value.TotalHours >= 1 ? value.ToString("hh\\:mm\\:ss\\,fff") : value.ToString("mm\\:ss\\,fff");
    }

    public override string ToString() => Text;
}