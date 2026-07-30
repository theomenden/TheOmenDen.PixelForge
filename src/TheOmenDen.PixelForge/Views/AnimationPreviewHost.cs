using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using SkiaSharp.Views.Windows;
using TheOmenDen.PixelForge.Core.Spritesheets;
using TheOmenDen.PixelForge.ViewModels;

namespace TheOmenDen.PixelForge.Views;

/// <summary>
/// Plays the selected asset's animation, one frame at a time.
/// </summary>
/// <remarks>
/// <para>
/// The third of this project's preview hosts, alongside <see cref="CompositePreview"/> and
/// <see cref="PalettePreviewHost"/>, and it follows the same rule: the view model deals in recipes
/// and clips, the host turns them into pixels, and the page owns neither.
/// </para>
/// <para>
/// Every frame of a clip is pre-rendered on selection rather than cropped on each tick. A clip is
/// at most five frames, the assembly they come from costs 828 KiB to hold, and dropping it straight
/// after means playback is a bitmap swap on a timer with nothing decoding behind it.
/// </para>
/// <para>
/// Frame order comes from <see cref="GeneratorClip.Frames"/>, which repeats and descends —
/// <c>walk</c> is 1, 2, 1, 0. Playing the distinct columns in ascending order, which is the obvious
/// mistake, gives a different animation.
/// </para>
/// </remarks>
/// <param name="viewModel">The page's view model; its selection and clip are what plays.</param>
/// <param name="image">The control frames are written to.</param>
/// <param name="hint">Shown in the image's place when there is nothing to play.</param>
internal sealed class AnimationPreviewHost(AssetsViewModel viewModel, Image image, TextBlock hint)
{
    /// <summary>
    /// Nearest-neighbour multiplier. 48px cells at 4x give a 192px preview, which reads clearly
    /// beside the grid without crowding it.
    /// </summary>
    private const int PreviewScale = 4;

    private readonly DispatcherQueueTimer _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
    private readonly List<WriteableBitmap> _frames = [];
    private int _index;

    /// <summary>Subscribes and renders the first clip.</summary>
    public void Start()
    {
        viewModel.PropertyChanged -= OnViewModelChanged;
        viewModel.PropertyChanged += OnViewModelChanged;

        _timer.Interval = TimeSpan.FromMilliseconds(GeneratorClips.FrameDurationMilliseconds);
        _timer.IsRepeating = true;
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;

        Rebuild();
    }

    /// <summary>Stops playback and drops the rendered frames.</summary>
    public void Stop()
    {
        viewModel.PropertyChanged -= OnViewModelChanged;

        _timer.Tick -= OnTick;
        _timer.Stop();

        _frames.Clear();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(AssetsViewModel.PreviewRecipe):
            case nameof(AssetsViewModel.SelectedPartial):
            case nameof(AssetsViewModel.SelectedClip):
            case nameof(AssetsViewModel.SelectedFacing):
            case nameof(AssetsViewModel.ShowOverBody):
                Rebuild();
                break;

            case nameof(AssetsViewModel.IsPlaying):
                Pump();
                break;

            default:
                break;
        }
    }

    /// <summary>Re-renders every frame of the current clip and restarts playback.</summary>
    private void Rebuild()
    {
        _frames.Clear();
        _index = 0;

        if (!viewModel.PreviewRecipe.TryGet(out var recipe))
        {
            ShowHint("Select an asset to preview its animation.");

            return;
        }

        var created = SpriteFilmstrip.Create(recipe);

        if (!created.TryGet(out var filmstrip))
        {
            ShowHint($"Preview unavailable: {created.Error}.");

            return;
        }

        using (filmstrip)
        {
            foreach (var column in viewModel.SelectedClip.Frames)
            {
                var rendered = filmstrip.RenderCell(viewModel.SelectedFacing, column, PreviewScale);

                if (!rendered.TryGet(out var cell))
                {
                    ShowHint($"Preview unavailable: {rendered.Error}.");

                    return;
                }

                using (cell)
                {
                    _frames.Add(cell.ToWriteableBitmap());
                }
            }
        }

        if (_frames.Count is 0)
        {
            ShowHint("That clip has no frames.");

            return;
        }

        hint.Visibility = Visibility.Collapsed;
        image.Visibility = Visibility.Visible;

        Draw();
        Pump();
    }

    /// <summary>Runs the timer only when there is more than one frame and the user wants motion.</summary>
    /// <remarks>
    /// A single-frame clip — <c>stand</c>, <c>crouch</c>, <c>sleep_dead</c> — is a pose. Ticking for
    /// it would redraw the same bitmap forever.
    /// </remarks>
    private void Pump()
    {
        if (viewModel.IsPlaying && _frames.Count > 1)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void OnTick(DispatcherQueueTimer sender, object args)
    {
        if (_frames.Count is 0)
        {
            return;
        }

        _index = (_index + 1) % _frames.Count;

        Draw();
    }

    private void Draw() => image.Source = _frames[_index];

    private void ShowHint(string text)
    {
        _timer.Stop();

        hint.Text = text;
        hint.Visibility = Visibility.Visible;
        image.Visibility = Visibility.Collapsed;
    }
}
