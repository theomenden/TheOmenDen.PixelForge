using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using TheOmenDen.PixelForge.Core.Palettes;

namespace TheOmenDen.PixelForge.ViewModels;

/// <summary>
/// One editable step of a ramp. <see cref="Color"/> and <see cref="Hex"/> are two views of the
/// same value and each keeps the other in step, so typing a hex moves the swatch and the picker
/// moves the text.
/// </summary>
public sealed partial class RampStepViewModel(int index, SKColor color) : ObservableObject
{
    /// <summary>0 = darkest shadow, 4 = lightest highlight.</summary>
    public int Index { get; } = index;

    public string Label => $"Step {Index + 1}";

    /// <summary>
    /// Automation ids come off the item, not the template: a DataTemplate cannot give each
    /// generated row a distinct id, and ui-tests.ps1 addresses these by name.
    /// </summary>
    public string SwatchAutomationId => $"SwatchStep{Index + 1}";

    public string HexAutomationId => $"HexStep{Index + 1}";

    /// <summary>
    /// Accessible name for the hex box. A plain x:Bind cannot mix a markup extension with literal
    /// text in one attribute value, so the " hex" suffix is computed here instead.
    /// </summary>
    public string HexFieldName => $"{Label} hex";

    /// <summary>Raised when either representation changes, so the preview can re-render.</summary>
    public event EventHandler? Changed;

    public SKColor Color
    {
        get => field;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(Hex));
            OnPropertyChanged(nameof(PickerColor));

            Changed?.Invoke(this, EventArgs.Empty);
        }
    } = color;

    /// <summary>
    /// The same colour as <see cref="Color"/>, in the type <c>ColorPickerButton.SelectedColor</c>
    /// binds to.
    /// <para>
    /// <c>Windows.UI.Color</c> is a four-byte WinRT struct — no dispatcher, no XAML dependency, no
    /// window affinity — so it does not compromise this view model's testability. Exposing it is
    /// what lets the picker two-way bind directly and removes the <c>ColorChanged</c> handler,
    /// the <c>Tag</c>-carried step index, and the <c>SetStepColor</c> hop through the parent view
    /// model that the first draft needed.
    /// </para>
    /// </summary>
    public Windows.UI.Color PickerColor
    {
        get => Windows.UI.Color.FromArgb(Color.Alpha, Color.Red, Color.Green, Color.Blue);
        set => Color = new SKColor(value.R, value.G, value.B, value.A);
    }

    /// <summary>
    /// Round-trips through the store's own parser, so what the editor accepts is exactly what a
    /// saved file can contain. An unparseable value is ignored rather than throwing — the user is
    /// mid-keystroke, not wrong.
    /// </summary>
    public string Hex
    {
        get => RampConversions.Hex(Color);
        set
        {
            if (RampConversions.TryParseHex(value, out var parsed))
            {
                Color = parsed;
            }
            else
            {
                // Push the canonical form back so the TextBox does not keep invalid text.
                OnPropertyChanged();
            }
        }
    }
}
