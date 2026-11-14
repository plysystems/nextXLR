"""Generates Styles/Motion.xaml (three control styles sharing one animated template).

Run after editing:  python apps/windows/Styles/motion.py

Each visual state is a Storyboard with zero-length animations; the VisualTransition's
GeneratedDuration (200 ms, cubic ease-in-out) interpolates between them. (Plain Setters would
switch instantly; only storyboard values are interpolated.)
"""
from pathlib import Path

NEUTRAL = dict(bg="#303030", stroke="#383838", fg="#EAEAEA")
HOVER = dict(bg="#3A3A3A", stroke="#484848")
PRESSED = dict(bg="#262626")

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

def template(target_type, states, hover_stroke=None):
    states_xml = "\n".join(states)
    return f'''        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="{target_type}">
                    <Grid x:Name="Root" CornerRadius="{{TemplateBinding CornerRadius}}" RenderTransformOrigin="0.5,0.5">
                        <Grid.RenderTransform><ScaleTransform x:Name="Scale" /></Grid.RenderTransform>
                        <Grid.Background><SolidColorBrush x:Name="Bg" Color="{NEUTRAL['bg']}" /></Grid.Background>
                        <Border CornerRadius="{{TemplateBinding CornerRadius}}" BorderThickness="{{TemplateBinding BorderThickness}}">
                            <Border.BorderBrush><SolidColorBrush x:Name="Stroke" Color="{NEUTRAL['stroke']}" /></Border.BorderBrush>
                        </Border>
                        <ContentPresenter Padding="{{TemplateBinding Padding}}" Content="{{TemplateBinding Content}}"
                                          ContentTemplate="{{TemplateBinding ContentTemplate}}"
                                          HorizontalContentAlignment="{{TemplateBinding HorizontalContentAlignment}}"
                                          VerticalContentAlignment="{{TemplateBinding VerticalContentAlignment}}">
                            <ContentPresenter.Foreground><SolidColorBrush x:Name="Fg" Color="{NEUTRAL['fg']}" /></ContentPresenter.Foreground>
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

def toggle_states(on, on_hover, on_pressed, on_fg):
    return [
        state("Normal", **NEUTRAL),
        state("PointerOver", bg=HOVER["bg"], stroke=HOVER["stroke"], fg=NEUTRAL["fg"]),
        state("Pressed", bg=PRESSED["bg"], stroke=NEUTRAL["stroke"], fg=NEUTRAL["fg"], press=True),
        state("Disabled", **NEUTRAL, opacity="0.45"),
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

def style(key, target, states):
    return f'''    <Style x:Key="{key}" TargetType="{target}">
{COMMON_SETTERS}
{template(target, states)}
    </Style>'''

button_states = [
    state("Normal", **NEUTRAL),
    state("PointerOver", bg=HOVER["bg"], stroke="#4FE0B8", fg=NEUTRAL["fg"]),
    state("Pressed", bg=PRESSED["bg"], stroke="#4FE0B8", fg=NEUTRAL["fg"], press=True),
    state("Disabled", **NEUTRAL, opacity="0.45"),
]

xml = f'''<?xml version="1.0" encoding="utf-8"?>
<!-- Generated by Styles/motion.py — edit there, not here.
     Motion: interactive surfaces ease between states in 200 ms (cubic ease-in-out); presses
     scale to 0.97. Used by tiles, routing cells, pads, banks and mute buttons. -->
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

{style("SmoothToggle", "ToggleButton", toggle_states("#4FE0B8", "#6FE8C7", "#3CCFA6", "#0E1015"))}

{style("SmoothDangerToggle", "ToggleButton", toggle_states("#F0506A", "#F46B82", "#D9455D", "#FFFFFF"))}

{style("SmoothButton", "Button", button_states)}
</ResourceDictionary>
'''
(Path(__file__).parent / "Motion.xaml").write_text(xml, encoding="utf-8")
print("Motion.xaml written")
