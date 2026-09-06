using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WindowsFileManager.ViewModels;

namespace WindowsFileManager.Views.Panels;

/// <summary>
/// File preview panel: image, video and audio preview with transport controls.
/// </summary>
[ExcludeFromCodeCoverage]
public partial class PreviewPanel : UserControl
{
    private double _videoVolumeBeforeMute = 0.5;
    private double _audioVolumeBeforeMute = 0.5;

    /// <summary>
    /// Initializes a new instance of the <see cref="PreviewPanel"/> class.
    /// </summary>
    public PreviewPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Stops both players. Called by the composition root when DuplicatesScreen reports a
    /// selection change — see <see cref="Screens.DuplicatesScreen.GroupSelectionChanged"/>.
    /// </summary>
    /// <remarks>
    /// Contractual, not incidental: the trigger is a <c>SelectionChanged</c> on the duplicate
    /// group list, not a ViewModel property change, and this method exists only because that
    /// reach-across could not survive the split into sibling controls. **T-008 owns its
    /// retirement** — when the duplicate group list is rebuilt, T-008 decides whether the
    /// stop stays an event through the composition root or becomes ViewModel state.
    /// </remarks>
    public void StopMedia()
    {
        // Stop any playing media when selection changes
        try
        {
            VideoPlayer.Stop();
            AudioPlayer.Stop();
        }
        catch
        {
            // Media elements may not be initialized yet
        }
    }

    private void MediaElement_MediaOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not MediaElement media)
        {
            return;
        }

        // Sync volume from sliders
        if (media == VideoPlayer)
        {
            media.Volume = VideoVolumeSlider.Value;
        }
        else if (media == AudioPlayer)
        {
            media.Volume = AudioVolumeSlider.Value;
        }

        media.Play();

        if (DataContext is MainViewModel vm && !vm.IsAutoPlay)
        {
            // Delay pause to let first frame render, then show as thumbnail
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                media.Pause();
                media.Position = TimeSpan.FromMilliseconds(100);
            });
        }
    }

    private void PlayMedia_Click(object sender, RoutedEventArgs e) => VideoPlayer.Play();

    private void PauseMedia_Click(object sender, RoutedEventArgs e) => VideoPlayer.Pause();

    private void StopMedia_Click(object sender, RoutedEventArgs e) => VideoPlayer.Stop();

    private void PlayAudio_Click(object sender, RoutedEventArgs e) => AudioPlayer.Play();

    private void PauseAudio_Click(object sender, RoutedEventArgs e) => AudioPlayer.Pause();

    private void StopAudio_Click(object sender, RoutedEventArgs e) => AudioPlayer.Stop();

    private void VideoVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        VideoPlayer.Volume = e.NewValue;
        VideoMuteButton.Content = e.NewValue < 0.01 ? "🔇" : "🔊";
    }

    private void AudioVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        AudioPlayer.Volume = e.NewValue;
        AudioMuteButton.Content = e.NewValue < 0.01 ? "🔇" : "🔊";
    }

    private void VideoMute_Click(object sender, RoutedEventArgs e)
    {
        if (VideoVolumeSlider.Value > 0.01)
        {
            _videoVolumeBeforeMute = VideoVolumeSlider.Value;
            VideoVolumeSlider.Value = 0;
        }
        else
        {
            VideoVolumeSlider.Value = _videoVolumeBeforeMute;
        }
    }

    private void AudioMute_Click(object sender, RoutedEventArgs e)
    {
        if (AudioVolumeSlider.Value > 0.01)
        {
            _audioVolumeBeforeMute = AudioVolumeSlider.Value;
            AudioVolumeSlider.Value = 0;
        }
        else
        {
            AudioVolumeSlider.Value = _audioVolumeBeforeMute;
        }
    }
}
