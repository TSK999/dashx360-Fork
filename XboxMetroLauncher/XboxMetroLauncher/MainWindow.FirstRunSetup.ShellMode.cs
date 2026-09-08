using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace XboxMetroLauncher;

public partial class MainWindow
{
    private DispatcherTimer? _firstRunShellModeTimer;
    private UIElement? _firstRunTopChrome;
    private bool _firstRunShellModeInstalled;
    private int _firstRunShellModeAttempts;

    internal void InstallFirstRunToplessShellMode()
    {
        if (_firstRunShellModeInstalled)
        {
            return;
        }

        _firstRunShellModeInstalled = true;
        ContentRendered += (_, _) =>
        {
            if (_firstRunShellModeTimer != null)
            {
                return;
            }

            _firstRunShellModeTimer = new DispatcherTimer(DispatcherPriority.Loaded)
            {
                Interval = TimeSpan.FromMilliseconds(90)
            };
            _firstRunShellModeTimer.Tick += (_, _) =>
            {
                if (_firstRunLayer == null)
                {
                    // Normal launches do not create the setup layer. Do not keep a
                    // timer alive forever just waiting for a first-run page.
                    if (++_firstRunShellModeAttempts >= 80)
                    {
                        _firstRunShellModeTimer?.Stop();
                    }
                    return;
                }

                _firstRunShellModeTimer?.Stop();
                ApplyFirstRunToplessShellMode();
            };
            _firstRunShellModeTimer.Start();
        };

        Closed += (_, _) => _firstRunShellModeTimer?.Stop();
    }

    private void ApplyFirstRunToplessShellMode()
    {
        if (_firstRunLayer == null)
        {
            return;
        }

        // The setup state owns the dashboard body from the top edge through the
        // content row. The bottom command strip remains visible for A/B prompts.
        _firstRunTopChrome = DashboardContentHost.Children
            .Cast<UIElement>()
            .FirstOrDefault(child =>
                Grid.GetRow(child) == 0
                && !ReferenceEquals(child, _firstRunHeaderBlocker)
                && !ReferenceEquals(child, _firstRunLayer));

        if (_firstRunTopChrome != null)
        {
            _firstRunTopChrome.Visibility = Visibility.Collapsed;
        }

        if (ReferenceEquals(_firstRunLayer.Parent, ContentFrame))
        {
            ContentFrame.Children.Remove(_firstRunLayer);
            DashboardContentHost.Children.Add(_firstRunLayer);
        }

        Grid.SetRow(_firstRunLayer, 0);
        Grid.SetRowSpan(_firstRunLayer, 2);
        Panel.SetZIndex(_firstRunLayer, 1000);
        _firstRunLayer.Width = 1280;
        _firstRunLayer.Height = 648;
        _firstRunLayer.HorizontalAlignment = HorizontalAlignment.Left;
        _firstRunLayer.VerticalAlignment = VerticalAlignment.Top;

        // Recompose the existing setup visuals for the taller shell-owned surface.
        if (_firstRunLayer.Children.Count > 2 && _firstRunLayer.Children[2] is Canvas stripes)
        {
            int index = 0;
            foreach (UIElement child in stripes.Children)
            {
                if (child is Rectangle stripe)
                {
                    stripe.Height = 205;
                    Canvas.SetLeft(stripe, 1128 + (index * 27));
                    Canvas.SetTop(stripe, 443);
                    index++;
                }
            }
        }

        if (_firstRunStepText != null)
        {
            _firstRunStepText.Margin = new Thickness(76, 34, 0, 0);
        }

        if (_firstRunLayer.Children.Count > 4 && _firstRunLayer.Children[4] is StackPanel progressWrap)
        {
            progressWrap.Margin = new Thickness(0, 38, 82, 0);
        }

        if (_firstRunLayer.Children.Count > 5 && _firstRunLayer.Children[5] is Grid main)
        {
            main.Margin = new Thickness(76, 104, 76, 44);

            if (main.Children.Count > 0 && main.Children[0] is Border info)
            {
                info.Height = 390;
                info.VerticalAlignment = VerticalAlignment.Center;
            }

            if (main.Children.Count > 1 && main.Children[1] is Border content)
            {
                content.Height = 390;
                content.VerticalAlignment = VerticalAlignment.Center;
            }
        }

        // The setup page itself now blocks the whole upper shell, so the old
        // transparent row-zero blocker is kept behind it and cannot tint the page.
        Panel.SetZIndex(_firstRunHeaderBlocker, 900);

        _firstRunLayer.IsVisibleChanged += (_, _) =>
        {
            if (_firstRunTopChrome == null || _firstRunLayer == null)
            {
                return;
            }

            // Keep normal dashboard chrome absent for the entire first-run state,
            // then restore it exactly when setup hands control back to Home.
            _firstRunTopChrome.Visibility = _firstRunLayer.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        };
    }
}
