using System.Reflection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace NextXLR.Services;

public static class CursorHelper
{
    private static readonly PropertyInfo? ProtectedCursorProp =
        typeof(UIElement).GetProperty("ProtectedCursor", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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
        if (d is UIElement element)
        {
            SetCursor(element, (bool)e.NewValue ? HandCursor : null);
        }
    }

    public static void SetCursor(UIElement element, InputCursor? cursor)
    {
        try
        {
            ProtectedCursorProp?.SetValue(element, cursor);
        }
        catch { }
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
            if (child is ButtonBase or ToggleSwitch or HyperlinkButton or ComboBox or RadioButton or CheckBox)
            {
                if (child is UIElement ui)
                {
                    SetCursor(ui, HandCursor);
                }
            }
            ApplyHandCursorRecursive(child);
        }
    }
}
