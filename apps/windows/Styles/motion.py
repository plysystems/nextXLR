"""Generates Styles/Motion.xaml (three control styles sharing one animated template).

Run after editing:  python apps/windows/Styles/motion.py

Each visual state is a Storyboard with zero-length animations; the VisualTransition's
GeneratedDuration (200 ms, cubic ease-in-out) interpolates between them. (Plain Setters would
switch instantly; only storyboard values are interpolated.)
"""
from pathlib import Path

DARK_NEUTRAL = dict(bg="#141416", stroke="#222226", fg="#F8FAFC")
DARK_HOVER = dict(bg="#1C1C20", stroke="#32323A")
DARK_PRESSED = dict(bg="#0A0A0A")

LIGHT_NEUTRAL = dict(bg="#FFFFFF", stroke="#D8DCE4", fg="#0F172A")
LIGHT_HOVER = dict(bg="#E7E9EF", stroke="#C8CED8")
LIGHT_PRESSED = dict(bg="#DCE0E8")

def anim(target, prop, value):
    if prop == "Color":
        return f'<ColorAnimation Storyboard.TargetName="{target}" Storyboard.TargetProperty="Color" To="{value}" Duration="0" />'
    return f'<DoubleAnimation Storyboard.TargetName="{target}" Storyboard.TargetProperty="{prop}" To="{value}" Duration="0" />'

def state(name, bg=None, stroke=None, fg=None, press=False, opacity=None):
    parts = []
    if bg: parts.append(anim("Bg", "Color", bg))
    if stroke: parts.append(anim("Stroke", "Color", stroke))
    if fg: parts.append(anim("Fg", "Color", fg))
    if press:
        parts += [anim("Scale", "ScaleX", "0.97"), anim("Scale", "ScaleY", "0.97")]
    if opacity is not None: parts.append(anim("Root", "Opacity", opacity))
    body = "\n".join(" " * 36 + p for p in parts)
    return f'''                                <VisualState x:Name="{name}">
                                    <Storyboard>
{body}
                                    </Storyboard>
                                </VisualState>'''

def template(target_type, states, neutral_cfg):
    states_xml = "\n".join(states)
    return f'''        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{target_type}">
                    <Grid x:Name="Root" CornerRadius="{{TemplateBinding CornerRadius}}" RenderTransformOrigin="0.5,0.5">
                        <Grid.RenderTransform><ScaleTransform x:Name="Scale" /></Grid.RenderTransform>
                        <Grid.Background><SolidColorBrush x:Name="Bg" Color="{neutral_cfg['bg']}" /></Grid.Background>
                        <Border CornerRadius="{{TemplateBinding CornerRadius}}" BorderThickness="{{TemplateBinding BorderThickness}}">
                            <Border.BorderBrush><SolidColorBrush x:Name="Stroke" Color="{neutral_cfg['stroke']}" /></Border.BorderBrush>
                        </Border>
                        <ContentPresenter Padding="{{TemplateBinding Padding}}" Content="{{TemplateBinding Content}}"
                                          ContentTemplate="{{TemplateBinding ContentTemplate}}"
                                          HorizontalContentAlignment="{{TemplateBinding HorizontalContentAlignment}}"
                                          VerticalContentAlignment="{{TemplateBinding VerticalContentAlignment}}">
                            <ContentPresenter.Foreground><SolidColorBrush x:Name="Fg" Color="{neutral_cfg['fg']}" /></ContentPresenter.Foreground>
                        </ContentPresenter>
                        <VisualStateManager.VisualStateGroups>
                            <VisualStateGroup x:Name="CommonStates">
                                <VisualStateGroup.Transitions>
                                    <VisualTransition GeneratedDuration="0:0:0.2">
                                        <VisualTransition.GeneratedEasingFunction><CubicEase EasingMode="EaseInOut" /></VisualTransition.GeneratedEasingFunction>
                                    </VisualTransition>
                                </VisualStateGroup.Transitions>
{states_xml}
                            </VisualStateGroup>
                        </VisualStateManager.VisualStateGroups>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>'''

def toggle_states(neutral, hover, pressed, on, on_hover, on_pressed, on_fg):
    return [
        state("Normal", **neutral),
        state("PointerOver", bg=hover["bg"], stroke=hover["stroke"], fg=neutral["fg"]),
        state("Pressed", bg=pressed["bg"], stroke=neutral["stroke"], fg=neutral["fg"], press=True),
        state("Disabled", **neutral, opacity="0.45"),
        state("Checked", bg=on, stroke=on, fg=on_fg),
        state("CheckedPointerOver", bg=on_hover, stroke=on_hover, fg=on_fg),
        state("CheckedPressed", bg=on_pressed, stroke=on_pressed, fg=on_fg, press=True),
        state("CheckedDisabled", bg=on, stroke=on, fg=on_fg, opacity="0.45"),
    ]

COMMON_SETTERS = '''        <Setter Property="BorderThickness" Value="1" />
        <Setter Property="CornerRadius" Value="10" />
        <Setter Property="Padding" Value="12,8" />
        <Setter Property="FontWeight" Value="SemiBold" />
        <Setter Property="HorizontalAlignment" Value="Stretch" />
        <Setter Property="VerticalAlignment" Value="Stretch" />
        <Setter Property="HorizontalContentAlignment" Value="Center" />
        <Setter Property="VerticalContentAlignment" Value="Center" />
        <Setter Property="UseSystemFocusVisuals" Value="True" />'''

def style(key, target, states, neutral_cfg):
    return f'''    <Style x:Key="{key}" TargetType="{target}">
{COMMON_SETTERS}
{template(target, states, neutral_cfg)}
    </Style>'''

def button_states(neutral, hover, pressed, accent_color):
    return [
        state("Normal", **neutral),
        state("PointerOver", bg=hover["bg"], stroke=accent_color, fg=neutral["fg"]),
        state("Pressed", bg=pressed["bg"], stroke=accent_color, fg=neutral["fg"], press=True),
        state("Disabled", **neutral, opacity="0.45"),
    ]

def generate_styles(neutral, hover, pressed, on, on_hover, on_pressed, on_fg, danger_on, danger_hover, danger_pressed, danger_fg, accent_color):
    toggle = style("SmoothToggle", "ToggleButton", toggle_states(neutral, hover, pressed, on, on_hover, on_pressed, on_fg), neutral)
    danger = style("SmoothDangerToggle", "ToggleButton", toggle_states(neutral, hover, pressed, danger_on, danger_hover, danger_pressed, danger_fg), neutral)
    btn = style("SmoothButton", "Button", button_states(neutral, hover, pressed, accent_color), neutral)
    return f"{toggle}\n\n{danger}\n\n{btn}"

dark_styles = generate_styles(
    DARK_NEUTRAL, DARK_HOVER, DARK_PRESSED,
    "#24C5BF", "#3DDFD9", "#1AA29D", "#050505",
    "#F0506A", "#F46B82", "#D9455D", "#FFFFFF",
    "#24C5BF"
)

light_styles = generate_styles(
    LIGHT_NEUTRAL, LIGHT_HOVER, LIGHT_PRESSED,
    "#168B87", "#1AA29D", "#137E7A", "#FFFFFF",
    "#EF4444", "#F87171", "#DC2626", "#FFFFFF",
    "#168B87"
)

xml = f'''<?xml version="1.0" encoding="utf-8"?>
<!-- Generated by Styles/motion.py — edit there, not here.
     Motion: interactive surfaces ease between states in 200 ms (cubic ease-in-out); presses
     scale to 0.97. Used by tiles, routing cells, pads, banks and mute buttons. -->
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <ResourceDictionary.ThemeDictionaries>
        <ResourceDictionary x:Key="Default">
{dark_styles}
        </ResourceDictionary>
        <ResourceDictionary x:Key="Dark">
{dark_styles}
        </ResourceDictionary>
        <ResourceDictionary x:Key="Light">
{light_styles}
        </ResourceDictionary>
    </ResourceDictionary.ThemeDictionaries>

</ResourceDictionary>
'''
(Path(__file__).parent / "Motion.xaml").write_text(xml, encoding="utf-8")
print("Motion.xaml written with Default, Dark and Light ThemeDictionaries")
