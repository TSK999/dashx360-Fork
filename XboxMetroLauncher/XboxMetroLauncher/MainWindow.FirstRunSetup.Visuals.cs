using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace XboxMetroLauncher;

public partial class MainWindow
{
    private DispatcherTimer? _firstRunVisualRefreshTimer;
    private int _firstRunVisualsAppliedStep = -1;
    private DependencyObject? _firstRunVisualsAppliedContent;
    private bool _firstRunVisualRefreshInstalled;

    internal void InstallFirstRunVisualRefresh()
    {
        if (_firstRunVisualRefreshInstalled)
        {
            return;
        }

        _firstRunVisualRefreshInstalled = true;
        ContentRendered += (_, _) => BeginFirstRunVisualRefresh();
    }

    private void BeginFirstRunVisualRefresh()
    {
        if (_firstRunVisualRefreshTimer != null)
        {
            return;
        }

        _firstRunVisualRefreshTimer = new DispatcherTimer(DispatcherPriority.Loaded)
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _firstRunVisualRefreshTimer.Tick += (_, _) =>
        {
            if (_firstRunLayer == null || _firstRunContentHost == null)
            {
                return;
            }

            _firstRunVisualRefreshTimer?.Stop();
            ApplyFirstRunVisualRefresh();
        };
        _firstRunVisualRefreshTimer.Start();
    }

    private void ApplyFirstRunVisualRefresh()
    {
        if (_firstRunLayer == null || _firstRunContentHost == null)
        {
            return;
        }

        // Make the setup page a real shell surface. Home itself cannot bleed
        // through while first-run setup owns the dashboard body.
        if (_firstRunLayer.Children.Count > 0 && _firstRunLayer.Children[0] is Rectangle backdrop)
        {
            backdrop.Fill = new LinearGradientBrush(
                Color.FromRgb(207, 211, 211),
                Color.FromRgb(164, 171, 172),
                new Point(0, 0),
                new Point(0, 1));
        }

        if (_firstRunLayer.Children.Count > 1 && _firstRunLayer.Children[1] is Rectangle glow)
        {
            glow.Fill = new RadialGradientBrush
            {
                Center = new Point(0.24, 0.10),
                GradientOrigin = new Point(0.24, 0.10),
                RadiusX = 0.78,
                RadiusY = 0.95,
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(112, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(35, 255, 255, 255), 0.48),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
                }
            };
        }

        if (_firstRunLayer.Children.Count > 2 && _firstRunLayer.Children[2] is Canvas stripes)
        {
            stripes.Opacity = 0.48;
            int index = 0;
            foreach (UIElement child in stripes.Children)
            {
                if (child is Rectangle stripe)
                {
                    stripe.Width = 30;
                    stripe.Height = 205;
                    stripe.Opacity = 0.72 - (index * 0.08);
                    Canvas.SetLeft(stripe, 1128 + (index * 27));
                    Canvas.SetTop(stripe, 350);
                    index++;
                }
            }
        }

        if (_firstRunStepText != null)
        {
            _firstRunStepText.Foreground = new SolidColorBrush(Color.FromRgb(44, 49, 50));
            _firstRunStepText.FontSize = 30;
            _firstRunStepText.Margin = new Thickness(76, 15, 0, 0);
        }

        if (_firstRunLayer.Children.Count > 4 && _firstRunLayer.Children[4] is StackPanel progressWrap)
        {
            progressWrap.Margin = new Thickness(0, 20, 82, 0);
            foreach (TextBlock label in FirstRunDescendants<TextBlock>(progressWrap))
            {
                label.Foreground = new SolidColorBrush(Color.FromRgb(70, 76, 77));
                label.FontWeight = FontWeights.SemiBold;
            }
        }

        if (_firstRunLayer.Children.Count > 5 && _firstRunLayer.Children[5] is Grid main)
        {
            main.Margin = new Thickness(76, 66, 76, 30);
            if (main.ColumnDefinitions.Count >= 3)
            {
                main.ColumnDefinitions[0].Width = new GridLength(300);
                main.ColumnDefinitions[1].Width = new GridLength(28);
            }

            if (main.Children.Count > 0 && main.Children[0] is Border info)
            {
                info.Height = 340;
                info.VerticalAlignment = VerticalAlignment.Center;
                info.Background = new LinearGradientBrush(
                    Color.FromRgb(35, 39, 40),
                    Color.FromRgb(22, 25, 26),
                    new Point(0, 0),
                    new Point(0, 1));
                info.BorderThickness = new Thickness(0);
                info.Padding = new Thickness(27, 25, 27, 24);
                info.Effect = new DropShadowEffect
                {
                    BlurRadius = 18,
                    ShadowDepth = 4,
                    Opacity = 0.24,
                    Color = Colors.Black
                };
            }

            if (main.Children.Count > 1 && main.Children[1] is Border content)
            {
                content.Height = 340;
                content.VerticalAlignment = VerticalAlignment.Center;
                content.Background = new SolidColorBrush(Color.FromRgb(71, 77, 79));
                content.BorderBrush = new SolidColorBrush(Color.FromArgb(95, 255, 255, 255));
                content.BorderThickness = new Thickness(1);
                content.Padding = new Thickness(42, 26, 42, 26);
                content.Effect = new DropShadowEffect
                {
                    BlurRadius = 20,
                    ShadowDepth = 4,
                    Opacity = 0.18,
                    Color = Colors.Black
                };
            }
        }

        if (_firstRunHeaderBlocker != null)
        {
            _firstRunHeaderBlocker.Background = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0));
        }

        if (_firstRunFooterLayer != null)
        {
            _firstRunFooterLayer.Background = new SolidColorBrush(Color.FromRgb(20, 22, 22));
        }

        _firstRunLayer.IsVisibleChanged += (_, _) =>
        {
            if (_firstRunLayer?.IsVisible == true)
            {
                ContentHost.BeginAnimation(OpacityProperty, null);
                ContentHost.Opacity = 0;
            }
        };

        _firstRunContentHost.LayoutUpdated += (_, _) => PolishCurrentFirstRunStep();
        PolishCurrentFirstRunStep();
    }

    private void PolishCurrentFirstRunStep()
    {
        if (_firstRunContentHost == null || _firstRunContentHost.Children.Count == 0)
        {
            return;
        }

        DependencyObject currentContent = _firstRunContentHost.Children[0];
        if (_firstRunVisualsAppliedStep == _firstRunStep
            && ReferenceEquals(_firstRunVisualsAppliedContent, currentContent))
        {
            return;
        }

        List<TextBlock> textBlocks = FirstRunDescendants<TextBlock>(currentContent).ToList();
        List<Button> buttons = FirstRunDescendants<Button>(currentContent).ToList();

        // RenderFirstRunStep adds the host panel before its controls. Do not mark a
        // step as polished until that page's visual tree actually exists.
        if (textBlocks.Count == 0 && buttons.Count == 0)
        {
            return;
        }

        if (_firstRunSectionDescription != null)
        {
            _firstRunSectionDescription.Text = _firstRunStep switch
            {
                0 => "A few quick choices will personalize DashX360 before you enter Home.",
                1 => "Choose the language for your dashboard.",
                2 => "Choose the region used by your local profile.",
                3 => "Choose the gamertag shown across the dashboard and Guide.",
                4 => "Check the display and connection DashX360 will use.",
                5 => "Bring your installed Steam games into My Games, or do it later.",
                _ => "Everything is saved. Enter Home when you're ready."
            };
        }

        bool compactChoicePage = _firstRunStep is 1 or 2;
        foreach (Button button in buttons)
        {
            button.Height = compactChoicePage ? 38 : 50;
            button.Margin = new Thickness(0, 0, 0, compactChoicePage ? 4 : 8);
            if (compactChoicePage)
            {
                button.FontSize = 18;
            }
        }

        if (_firstRunStep == 0)
        {
            foreach (TextBlock text in textBlocks)
            {
                if (string.Equals(text.Text, "Welcome to DashX360", StringComparison.Ordinal))
                {
                    text.FontSize = 40;
                }
                else if (text.Text.StartsWith("Your first-start setup is part", StringComparison.Ordinal))
                {
                    text.Text = "Set up your profile, region, and game library. You can change these later in Settings.";
                    text.FontSize = 18;
                    text.LineHeight = 27;
                    text.Width = 590;
                    text.HorizontalAlignment = HorizontalAlignment.Left;
                }
            }

            foreach (Button button in buttons)
            {
                if (string.Equals(button.Content as string, "begin setup", StringComparison.Ordinal))
                {
                    button.Content = "start";
                    button.Width = 330;
                }
            }
        }

        if (_firstRunStep is 1 or 2)
        {
            string expectedHeading = _firstRunStep == 1 ? "choose your language" : "where are you?";
            TextBlock? heading = textBlocks.FirstOrDefault(text => string.Equals(text.Text, expectedHeading, StringComparison.Ordinal));
            if (heading != null)
            {
                heading.FontSize = 24;
                heading.Margin = new Thickness(0, 0, 0, 8);
            }
        }

        if (_firstRunStep == 6)
        {
            foreach (Button button in buttons)
            {
                if (string.Equals(button.Content as string, "enter dashboard", StringComparison.Ordinal))
                {
                    button.Content = "enter Home";
                }
            }
        }

        _firstRunVisualsAppliedStep = _firstRunStep;
        _firstRunVisualsAppliedContent = currentContent;
    }

    private static IEnumerable<T> FirstRunDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (T descendant in FirstRunDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
