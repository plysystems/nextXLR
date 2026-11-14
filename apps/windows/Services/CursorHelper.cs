using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace NextXLR.Services;

public static class CursorHelper
{
    [DllImport("user32.dll")]
    private static extern nint SetCursor(nint hCursor);

    [DllImport("user32.dll")]
    private static extern nint LoadCursor(nint hInstance, int lpCursorName);

    private const int IDC_HAND = 32649;
    private static readonly nint HandCursorHandle = LoadCursor(nint.Zero, IDC_HAND);

    private static readonly PropertyInfo? ProtectedCursorProp =
        typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
        typeof(UIElement).GetProperty("ProtectedCursor");

    private static readonly InputSystemCursor HandCursor = InputSystemCursor.Create(InputSystemCursorShape.Hand);

    public static readonly DependencyProperty UseHandCursorProperty =
        DependencyProperty.RegisterAttached(
            "UseHandCursor",
            typeof(bool),
            typeof(CursorHelper),
            new PropertyMetadata(false, OnUseHandCursorChanged));

    public static bool GetUseHandCursor(DependencyObject obj) => (bool)obj.GetValue(UseHandCursorProperty);
    public static void SetUseHandCursor(DependencyObject obj, bool value) => obj.SetValue(UseHandCursorProperty, value);

    private static void OnUseHandCursorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is UIElement element && (bool)e.NewValue)
        {
            AttachHandCursor(element);
        }
    }

    public static void AttachHandCursor(UIElement element)
    {
        try
        {
            ProtectedCursorProp?.SetValue(element, HandCursor);
        }
        catch { }

        element.PointerEntered -= OnPointerEnteredOrMoved;
        element.PointerEntered += OnPointerEnteredOrMoved;
        element.PointerMoved -= OnPointerEnteredOrMoved;
        element.PointerMoved += OnPointerEnteredOrMoved;
    }

    private static void OnPointerEnteredOrMoved(object sender, PointerRoutedEventArgs e)
    {
        if (HandCursorHandle != nint.Zero)
        {
            SetCursor(HandCursorHandle);
        }
    }

    /// <summary>
    /// Traverses the visual tree starting from <paramref name="root"/> and assigns
    /// a Hand cursor to all interactive elements (buttons, toggles, combos, checkboxes).
    /// </summary>
    public static void ApplyHandCursor(DependencyObject? root)
    {
        if (root == null) return;
        try
        {
            ApplyHandCursorRecursive(root);
        }
        catch { }
    }

    private static void ApplyHandCursorRecursive(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ButtonBase or ToggleSwitch or HyperlinkButton or ComboBox or RadioButton or CheckBox or Slider)
            {
                if (child is UIElement ui)
                {
                    AttachHandCursor(ui);
                }
            }
            ApplyHandCursorRecursive(child);
        }
    }
}
