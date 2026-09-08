using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using XboxMetroLauncher.Input;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Services;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher;

public partial class MainWindow
{
    private readonly CancellationTokenSource _firstRunLifetime = new();
    private DispatcherTimer? _firstRunReadyTimer;
    private ControllerInputService? _firstRunController;
    private FirstRunSetupService? _firstRunSetupService;
    private IGameLibraryService? _firstRunLibraryService;
    private ISteamLibraryScannerService? _firstRunSteamScanner;
    private IProfileService? _firstRunProfileService;
    private FirstRunSetupState _firstRunState = new();

    private Grid? _firstRunLayer;
    private Grid? _firstRunFooterLayer;
    private Grid? _firstRunHeaderBlocker;
    private Grid? _firstRunContentHost;
    private TextBlock? _firstRunStepText;
    private TextBlock? _firstRunSectionTitle;
    private TextBlock? _firstRunSectionDescription;
    private StackPanel? _firstRunProgressSegments;
    private TextBlock? _firstRunAcceptHint;
    private Border? _firstRunBackBadge;
    private TextBlock? _firstRunBackHint;
    private TextBox? _firstRunGamertagBox;
    private ProgressBar? _firstRunSteamProgress;
    private TextBlock? _firstRunSteamStatus;
    private Button? _firstRunSteamScanButton;
    private Button? _firstRunSteamContinueButton;
    private Control? _firstRunPreferredFocus;

    private bool _firstRunBootstrapStarted;
    private bool _firstRunSetupActive;
    private bool _firstRunBusy;
    private bool _firstRunSteamScanCompleted;
    private string _firstRunSteamMessage = string.Empty;
    private int _firstRunStep;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_firstRunBootstrapStarted)
        {
            return;
        }

        _firstRunBootstrapStarted = true;
        Closed += (_, _) => DisposeFirstRunSetup();
        _ = PrepareFirstRunSetupAsync();
    }

    private async Task PrepareFirstRunSetupAsync()
    {
        try
        {
            var store = new JsonStore(AppPaths.UserDataFolder);
            _firstRunSetupService = new FirstRunSetupService(store);
            bool forceSetup = Environment.GetCommandLineArgs().Skip(1).Any(arg =>
                string.Equals(arg, "--setup", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--first-run", StringComparison.OrdinalIgnoreCase));

            if (!forceSetup && _firstRunSetupService.IsCompleted())
            {
                return;
            }

            // Keep the real boot path, but reserve its hand-off for the setup page
            // instead of the normal fake-loading/sign-in sequence on this launch.
            _fakeLoadingStarted = true;
            _signInToastSequenceActive = true;

            _firstRunLibraryService = new JsonGameLibraryService(store);
            _firstRunSteamScanner = new SteamLibraryScannerService();
            _firstRunProfileService = new ProfileService(store);
            _firstRunState = await _firstRunSetupService.LoadAsync(_firstRunLifetime.Token);

            BuildFirstRunShellPage();

            _firstRunReadyTimer = new DispatcherTimer(DispatcherPriority.Loaded)
            {
                Interval = TimeSpan.FromMilliseconds(80)
            };
            _firstRunReadyTimer.Tick += (_, _) =>
            {
                if (_startupInitializationComplete && !_viewModel.IsBooting)
                {
                    _firstRunReadyTimer?.Stop();
                    ShowFirstRunSetup();
                }
            };
            _firstRunReadyTimer.Start();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            App.LogException(ex, "MainWindow.FirstRun.Prepare");
            _fakeLoadingStarted = false;
            _signInToastSequenceActive = false;
        }
    }

    private void BuildFirstRunShellPage()
    {
        if (_firstRunLayer != null)
        {
            return;
        }

        Brush green = FindSetupBrush("MetroGreenBrush", new SolidColorBrush(Color.FromRgb(2, 141, 2)));
        Brush glass = FindSetupBrush("GlassPanelBrush", new SolidColorBrush(Color.FromArgb(170, 25, 28, 29)));

        // This layer lives in ContentFrame, not RootGrid: the dashboard's real tab
        // strip, profile chrome and window remain visible around setup.
        var root = new Grid
        {
            Width = 1280,
            Height = 502,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            Opacity = 0
        };
        _firstRunLayer = root;
        Panel.SetZIndex(root, 900);

        root.Children.Add(new Rectangle
        {
            Fill = new LinearGradientBrush(
                Color.FromArgb(238, 48, 53, 55),
                Color.FromArgb(244, 93, 98, 99),
                new Point(0, 0),
                new Point(0, 1))
        });
        root.Children.Add(new Rectangle
        {
            IsHitTestVisible = false,
            Fill = new RadialGradientBrush
            {
                Center = new Point(0.18, 0.12),
                GradientOrigin = new Point(0.18, 0.12),
                RadiusX = 0.8,
                RadiusY = 0.9,
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(75, 255, 255, 255), 0),
                    new GradientStop(Color.FromArgb(20, 255, 255, 255), 0.48),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1)
                }
            }
        });

        var stripeCanvas = new Canvas { IsHitTestVisible = false, Opacity = 0.8 };
        for (int i = 0; i < 4; i++)
        {
            var stripe = new Rectangle
            {
                Width = 44,
                Height = 255,
                Fill = i == 2 ? new SolidColorBrush(Color.FromRgb(166, 218, 0)) : green,
                Opacity = 0.68 - (i * 0.08),
                RenderTransform = new SkewTransform(-18, 0),
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            Canvas.SetLeft(stripe, 1070 + (i * 35));
            Canvas.SetTop(stripe, -122);
            stripeCanvas.Children.Add(stripe);
        }
        root.Children.Add(stripeCanvas);

        _firstRunStepText = new TextBlock
        {
            Text = "initial setup",
            Foreground = Brushes.White,
            FontSize = 28,
            FontWeight = FontWeights.Light,
            Margin = new Thickness(76, 18, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        root.Children.Add(_firstRunStepText);

        var progressWrap = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 20, 78, 0)
        };
        progressWrap.Children.Add(new TextBlock
        {
            Text = "SETUP PROGRESS",
            Foreground = new SolidColorBrush(Color.FromArgb(185, 255, 255, 255)),
            FontSize = 11,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 0, 0, 7)
        });
        _firstRunProgressSegments = new StackPanel { Orientation = Orientation.Horizontal };
        progressWrap.Children.Add(_firstRunProgressSegments);
        root.Children.Add(progressWrap);

        var main = new Grid { Margin = new Thickness(76, 60, 76, 24) };
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(main);

        var infoBorder = new Border
        {
            Background = glass,
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(27, 25, 27, 22),
            CornerRadius = new CornerRadius(2),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.2 }
        };
        var infoStack = new StackPanel();
        infoStack.Children.Add(new TextBlock
        {
            Text = "FIRST START",
            Foreground = new SolidColorBrush(Color.FromArgb(175, 255, 255, 255)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold
        });
        _firstRunSectionTitle = new TextBlock
        {
            Text = "Welcome",
            Foreground = Brushes.White,
            FontSize = 38,
            FontWeight = FontWeights.Light,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 14)
        };
        infoStack.Children.Add(_firstRunSectionTitle);
        infoStack.Children.Add(new Rectangle { Height = 5, Fill = green, Margin = new Thickness(0, 0, 0, 18) });
        _firstRunSectionDescription = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
            FontSize = 17,
            FontWeight = FontWeights.Light,
            LineHeight = 25,
            TextWrapping = TextWrapping.Wrap
        };
        infoStack.Children.Add(_firstRunSectionDescription);
        infoBorder.Child = infoStack;
        main.Children.Add(infoBorder);

        var contentBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(48, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(36, 22, 36, 22),
            CornerRadius = new CornerRadius(2)
        };
        _firstRunContentHost = new Grid { ClipToBounds = true };
        contentBorder.Child = _firstRunContentHost;
        Grid.SetColumn(contentBorder, 2);
        main.Children.Add(contentBorder);

        ContentFrame.Children.Add(root);

        // Keep the shell's tab/header chrome visible but prevent it receiving clicks.
        _firstRunHeaderBlocker = new Grid
        {
            Background = Brushes.Transparent,
            Visibility = Visibility.Collapsed,
            Focusable = false
        };
        Grid.SetRow(_firstRunHeaderBlocker, 0);
        Panel.SetZIndex(_firstRunHeaderBlocker, 900);
        DashboardContentHost.Children.Add(_firstRunHeaderBlocker);

        // Replace the normal A Select / Y Eject strip while setup is active.
        _firstRunFooterLayer = new Grid
        {
            Background = new SolidColorBrush(Color.FromArgb(232, 17, 19, 19)),
            Visibility = Visibility.Collapsed,
            Opacity = 0
        };
        Grid.SetRow(_firstRunFooterLayer, 2);
        Panel.SetZIndex(_firstRunFooterLayer, 900);

        var footerGrid = new Grid { Margin = new Thickness(112, 0, 96, 0) };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _firstRunFooterLayer.Children.Add(footerGrid);

        var hints = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        hints.Children.Add(CreatePromptBadge("A", green));
        _firstRunAcceptHint = new TextBlock
        {
            Text = "Select",
            Foreground = Brushes.White,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 24, 0)
        };
        hints.Children.Add(_firstRunAcceptHint);
        _firstRunBackBadge = CreatePromptBadge("B", new SolidColorBrush(Color.FromRgb(210, 58, 50)));
        hints.Children.Add(_firstRunBackBadge);
        _firstRunBackHint = new TextBlock
        {
            Text = "Back",
            Foreground = Brushes.White,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0)
        };
        hints.Children.Add(_firstRunBackHint);
        footerGrid.Children.Add(hints);

        var shellLabel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        shellLabel.Children.Add(new Ellipse { Width = 10, Height = 10, Fill = green, Margin = new Thickness(0, 0, 7, 0) });
        shellLabel.Children.Add(new TextBlock
        {
            Text = "initial setup",
            Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
            FontSize = 14,
            FontWeight = FontWeights.Light
        });
        Grid.SetColumn(shellLabel, 1);
        footerGrid.Children.Add(shellLabel);

        DashboardContentHost.Children.Add(_firstRunFooterLayer);
        UpdateFirstRunProgress();
    }

    private void ShowFirstRunSetup()
    {
        if (_firstRunLayer == null || _firstRunFooterLayer == null || _firstRunHeaderBlocker == null || _firstRunSetupActive)
        {
            return;
        }

        _firstRunSetupActive = true;
        _isMenuFakeLoadingActive = true;
        ContentHost.IsHitTestVisible = false;
        ContentHost.Opacity = 0.12;

        _firstRunHeaderBlocker.Visibility = Visibility.Visible;
        _firstRunLayer.Visibility = Visibility.Visible;
        _firstRunLayer.IsHitTestVisible = true;
        _firstRunFooterLayer.Visibility = Visibility.Visible;

        AddHandler(Keyboard.PreviewKeyDownEvent, new KeyEventHandler(FirstRunPreviewKeyDown), true);

        _firstRunController = new ControllerInputService(
            HandleFirstRunControllerAction,
            () => _firstRunSetupActive && !_firstRunBusy && IsVisible && IsActive);
        _firstRunController.Start();

        RenderFirstRunStep(0, 1);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        _firstRunLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) { EasingFunction = easing });
        _firstRunFooterLayer.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = easing });
        _ = RefocusFirstRunAsync();
    }

    private async Task RefocusFirstRunAsync()
    {
        await Task.Delay(140);
        if (_firstRunSetupActive)
        {
            _firstRunPreferredFocus?.Focus();
        }
    }

    private void RenderFirstRunStep(int step, int direction)
    {
        if (_firstRunContentHost == null)
        {
            return;
        }

        _firstRunStep = Math.Clamp(step, 0, 6);
        _firstRunGamertagBox = null;
        _firstRunSteamProgress = null;
        _firstRunSteamStatus = null;
        _firstRunSteamScanButton = null;
        _firstRunSteamContinueButton = null;
        _firstRunPreferredFocus = null;

        var panel = new Grid { Opacity = 0 };
        panel.RenderTransform = new TranslateTransform(direction >= 0 ? 38 : -38, 0);
        _firstRunContentHost.Children.Clear();
        _firstRunContentHost.Children.Add(panel);

        switch (_firstRunStep)
        {
            case 0:
                SetFirstRunHeading("initial setup", "Welcome", "Before Home unlocks, choose a few essentials and optionally bring in your Steam library. The dashboard shell stays with you the whole time.");
                panel.Children.Add(BuildWelcomeStep());
                break;
            case 1:
                SetFirstRunHeading("1 of 6  ·  language", "Language", "Choose the language you want DashX360 to remember for this profile.");
                panel.Children.Add(BuildChoiceStep("choose your language", new[] { "English", "Deutsch", "Español", "Français", "Italiano", "Português" }, _firstRunState.Language, value => _firstRunState.Language = value));
                break;
            case 2:
                SetFirstRunHeading("2 of 6  ·  location", "Location", "Your location is used for the local profile and regional presentation.");
                panel.Children.Add(BuildChoiceStep("where are you?", new[] { "United States", "United Kingdom", "Sweden", "Germany", "France", "Canada", "Australia" }, _firstRunState.Locale, value => _firstRunState.Locale = value));
                break;
            case 3:
                SetFirstRunHeading("3 of 6  ·  profile", "Your profile", "Pick the gamertag that appears in the dashboard header, Guide and local social surfaces.");
                panel.Children.Add(BuildProfileStep());
                break;
            case 4:
                SetFirstRunHeading("4 of 6  ·  system", "System", "DashX360 uses your Windows display and network configuration. Nothing here replaces system settings.");
                panel.Children.Add(BuildSystemStep());
                break;
            case 5:
                SetFirstRunHeading("5 of 6  ·  games", "Game library", "Let DashX360 discover installed Steam games now, or skip and scan later from Settings.");
                panel.Children.Add(BuildSteamStep());
                break;
            default:
                SetFirstRunHeading("6 of 6  ·  complete", "You're ready", "Setup is saved. One last press and this page slides away to reveal the Home dashboard already underneath it.");
                panel.Children.Add(BuildReadyStep());
                break;
        }

        UpdateFirstRunProgress();
        UpdateFirstRunFooter();

        var transform = (TranslateTransform)panel.RenderTransform;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190)) { EasingFunction = easing });
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(transform.X, 0, TimeSpan.FromMilliseconds(225)) { EasingFunction = easing });

        _firstRunPreferredFocus?.Focus();
    }

    private UIElement BuildWelcomeStep()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) };
        stack.Children.Add(new TextBlock
        {
            Text = "Welcome to DashX360",
            Foreground = Brushes.White,
            FontSize = 42,
            FontWeight = FontWeights.Light,
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Your first-start setup is part of the dashboard now — no separate setup window.",
            Foreground = new SolidColorBrush(Color.FromArgb(205, 255, 255, 255)),
            FontSize = 19,
            FontWeight = FontWeights.Light,
            Margin = new Thickness(0, 10, 0, 26),
            TextWrapping = TextWrapping.Wrap
        });
        var begin = CreateSetupButton("begin setup", true);
        begin.Width = 360;
        begin.Click += async (_, _) => await GoFirstRunNextAsync();
        stack.Children.Add(begin);
        _firstRunPreferredFocus = begin;
        return stack;
    }

    private UIElement BuildChoiceStep(string heading, IReadOnlyList<string> values, string selected, Action<string> setValue)
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = heading,
            Foreground = Brushes.White,
            FontSize = 29,
            FontWeight = FontWeights.Light,
            Margin = new Thickness(0, 0, 0, 12)
        });

        foreach (string value in values)
        {
            bool isSelected = string.Equals(value, selected, StringComparison.OrdinalIgnoreCase);
            Button button = CreateSetupButton(value, isSelected);
            button.Width = 520;
            button.Tag = value;
            button.Click += async (_, _) =>
            {
                setValue(value);
                await SaveFirstRunProgressAsync();
                RenderFirstRunStep(_firstRunStep + 1, 1);
            };
            stack.Children.Add(button);
            if (_firstRunPreferredFocus == null || isSelected)
            {
                _firstRunPreferredFocus = button;
            }
        }

        return stack;
    }

    private UIElement BuildProfileStep()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = "create your local profile",
            Foreground = Brushes.White,
            FontSize = 29,
            FontWeight = FontWeights.Light
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Gamertag",
            Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
            FontSize = 14,
            Margin = new Thickness(0, 22, 0, 7)
        });

        _firstRunGamertagBox = new TextBox
        {
            Text = string.IsNullOrWhiteSpace(_firstRunState.Gamertag) ? "Player" : _firstRunState.Gamertag,
            Width = 520,
            Height = 56,
            HorizontalAlignment = HorizontalAlignment.Left,
            FontSize = 24,
            FontWeight = FontWeights.Light,
            Padding = new Thickness(14, 7, 14, 7),
            MaxLength = 15,
            Background = new SolidColorBrush(Color.FromArgb(238, 255, 255, 255)),
            Foreground = new SolidColorBrush(Color.FromRgb(35, 38, 39)),
            BorderBrush = FindSetupBrush("MetroGreenBrush", Brushes.Green),
            BorderThickness = new Thickness(2)
        };
        stack.Children.Add(_firstRunGamertagBox);

        var next = CreateSetupButton("continue", true);
        next.Width = 280;
        next.Margin = new Thickness(0, 18, 0, 0);
        next.Click += async (_, _) => await GoFirstRunNextAsync();
        stack.Children.Add(next);
        _firstRunPreferredFocus = _firstRunGamertagBox;
        return stack;
    }

    private UIElement BuildSystemStep()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = "system check",
            Foreground = Brushes.White,
            FontSize = 29,
            FontWeight = FontWeights.Light,
            Margin = new Thickness(0, 0, 0, 14)
        });

        var card = new Border
        {
            Width = 550,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromArgb(218, 255, 255, 255)),
            Padding = new Thickness(20),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
            BorderThickness = new Thickness(1)
        };
        var details = new StackPanel();
        details.Children.Add(CreateSystemLabel("DISPLAY"));
        details.Children.Add(CreateSystemValue($"{Math.Round(SystemParameters.PrimaryScreenWidth)} × {Math.Round(SystemParameters.PrimaryScreenHeight)}"));
        details.Children.Add(CreateSystemLabel("NETWORK", new Thickness(0, 13, 0, 3)));
        details.Children.Add(CreateSystemValue(NetworkInterface.GetIsNetworkAvailable() ? "Connected" : "Not connected"));
        card.Child = details;
        stack.Children.Add(card);

        var next = CreateSetupButton("looks good", true);
        next.Width = 280;
        next.Margin = new Thickness(0, 16, 0, 0);
        next.Click += async (_, _) => await GoFirstRunNextAsync();
        stack.Children.Add(next);
        _firstRunPreferredFocus = next;
        return stack;
    }

    private UIElement BuildSteamStep()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = "find your games",
            Foreground = Brushes.White,
            FontSize = 29,
            FontWeight = FontWeights.Light
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Scan installed Steam libraries and add detected titles to My Games.",
            Foreground = new SolidColorBrush(Color.FromArgb(205, 255, 255, 255)),
            FontSize = 17,
            TextWrapping = TextWrapping.Wrap,
            Width = 570,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 17)
        });

        _firstRunSteamScanButton = CreateSetupButton(_firstRunSteamScanCompleted ? "scan again" : "scan Steam library", true);
        _firstRunSteamScanButton.Width = 390;
        _firstRunSteamScanButton.Click += async (_, _) => await ScanSteamForFirstRunAsync();
        stack.Children.Add(_firstRunSteamScanButton);

        _firstRunSteamContinueButton = CreateSetupButton(_firstRunSteamScanCompleted ? "continue" : "skip for now", false);
        _firstRunSteamContinueButton.Width = 390;
        _firstRunSteamContinueButton.Click += async (_, _) =>
        {
            _firstRunState.ImportSteamLibrary = _firstRunSteamScanCompleted;
            await SaveFirstRunProgressAsync();
            RenderFirstRunStep(6, 1);
        };
        stack.Children.Add(_firstRunSteamContinueButton);

        _firstRunSteamProgress = new ProgressBar
        {
            Width = 570,
            Height = 8,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 6),
            IsIndeterminate = true,
            Visibility = Visibility.Collapsed,
            Foreground = FindSetupBrush("MetroGreenBrush", Brushes.Green)
        };
        stack.Children.Add(_firstRunSteamProgress);

        _firstRunSteamStatus = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(_firstRunSteamMessage)
                ? (_firstRunSteamScanCompleted ? "Steam library checked and saved." : "You can run this later from Settings → Steam.")
                : _firstRunSteamMessage,
            Foreground = new SolidColorBrush(Color.FromArgb(185, 255, 255, 255)),
            FontSize = 15,
            TextWrapping = TextWrapping.Wrap,
            Width = 570,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        stack.Children.Add(_firstRunSteamStatus);
        _firstRunPreferredFocus = _firstRunSteamScanButton;
        return stack;
    }

    private UIElement BuildReadyStep()
    {
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(new TextBlock
        {
            Text = "You're ready.",
            Foreground = Brushes.White,
            FontSize = 44,
            FontWeight = FontWeights.Light
        });
        stack.Children.Add(new TextBlock
        {
            Text = $"{NormalizeFirstRunGamertag(_firstRunState.Gamertag)}  ·  {_firstRunState.Locale}\n\n{(_firstRunSteamScanCompleted ? "Steam library checked." : "Steam scan skipped for now.")}",
            Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
            FontSize = 19,
            FontWeight = FontWeights.Light,
            TextWrapping = TextWrapping.Wrap,
            Width = 570,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 13, 0, 24)
        });
        var finish = CreateSetupButton("enter dashboard", true);
        finish.Width = 360;
        finish.Click += async (_, _) => await FinishFirstRunSetupAsync();
        stack.Children.Add(finish);
        _firstRunPreferredFocus = finish;
        return stack;
    }

    private async Task GoFirstRunNextAsync()
    {
        if (_firstRunBusy)
        {
            return;
        }

        await SaveFirstRunProgressAsync();
        if (_firstRunStep < 6)
        {
            RenderFirstRunStep(_firstRunStep + 1, 1);
        }
    }

    private async Task SaveFirstRunProgressAsync()
    {
        if (_firstRunSetupService == null)
        {
            return;
        }

        if (_firstRunGamertagBox != null)
        {
            _firstRunState.Gamertag = NormalizeFirstRunGamertag(_firstRunGamertagBox.Text);
        }

        await _firstRunSetupService.SaveAsync(_firstRunState, _firstRunLifetime.Token);
    }

    private async Task ScanSteamForFirstRunAsync()
    {
        if (_firstRunBusy || _firstRunLibraryService == null || _firstRunSteamScanner == null)
        {
            return;
        }

        _firstRunBusy = true;
        UpdateFirstRunFooter();
        if (_firstRunSteamScanButton != null) _firstRunSteamScanButton.IsEnabled = false;
        if (_firstRunSteamContinueButton != null) _firstRunSteamContinueButton.IsEnabled = false;
        if (_firstRunSteamProgress != null) _firstRunSteamProgress.Visibility = Visibility.Visible;
        if (_firstRunSteamStatus != null) _firstRunSteamStatus.Text = "Looking for installed Steam games...";

        try
        {
            GameLibrary library = await _firstRunLibraryService.LoadAsync(_firstRunLifetime.Token);
            var progress = new Progress<LibraryScanProgress>(p =>
            {
                if (_firstRunSteamStatus == null) return;
                _firstRunSteamStatus.Text = string.IsNullOrWhiteSpace(p.Location)
                    ? $"Scanning... {p.Completed}"
                    : $"Scanning {p.Location}";
            });
            SteamGameScanResult result = await _firstRunSteamScanner.ScanAsync(library, _firstRunLifetime.Token, progress);
            await _firstRunLibraryService.SaveAsync(library, _firstRunLifetime.Token);
            _firstRunSteamScanCompleted = true;
            _firstRunState.ImportSteamLibrary = true;
            _firstRunSteamMessage = result.Message;
            await SaveFirstRunProgressAsync();
            if (_firstRunSteamStatus != null) _firstRunSteamStatus.Text = result.Message;
            if (_firstRunSteamScanButton != null) _firstRunSteamScanButton.Content = "scan again";
            if (_firstRunSteamContinueButton != null) _firstRunSteamContinueButton.Content = "continue";
            _audioService.Play("select");
        }
        catch (OperationCanceledException)
        {
            _firstRunSteamMessage = "Steam scan cancelled.";
            if (_firstRunSteamStatus != null) _firstRunSteamStatus.Text = _firstRunSteamMessage;
        }
        catch (Exception ex)
        {
            App.LogException(ex, "MainWindow.FirstRun.SteamScan");
            _firstRunSteamMessage = "Steam couldn't be scanned right now. You can try again later from Settings.";
            if (_firstRunSteamStatus != null) _firstRunSteamStatus.Text = _firstRunSteamMessage;
        }
        finally
        {
            _firstRunBusy = false;
            if (_firstRunSteamScanButton != null) _firstRunSteamScanButton.IsEnabled = true;
            if (_firstRunSteamContinueButton != null) _firstRunSteamContinueButton.IsEnabled = true;
            if (_firstRunSteamProgress != null) _firstRunSteamProgress.Visibility = Visibility.Collapsed;
            UpdateFirstRunFooter();
            _firstRunSteamContinueButton?.Focus();
        }
    }

    private async Task FinishFirstRunSetupAsync()
    {
        if (_firstRunBusy || _firstRunSetupService == null || _firstRunProfileService == null)
        {
            return;
        }

        _firstRunBusy = true;
        UpdateFirstRunFooter();
        try
        {
            await SaveFirstRunProgressAsync();
            _firstRunState.Gamertag = NormalizeFirstRunGamertag(_firstRunState.Gamertag);

            Profile profile = await _firstRunProfileService.LoadAsync(_firstRunLifetime.Token);
            profile.Gamertag = _firstRunState.Gamertag;
            profile.Location = _firstRunState.Locale;
            await _firstRunProfileService.SaveAsync(profile, _firstRunLifetime.Token);

            _firstRunState.Completed = true;
            await _firstRunSetupService.SaveAsync(_firstRunState, _firstRunLifetime.Token);

            // Refresh profile and library before the page reveals Home so the shell's
            // existing bindings immediately reflect setup choices and any Steam import.
            await _viewModel.InitializeAsync(reloadSettings: false);
            UpdateThemeBackgroundVisual(animate: false);
            UpdateBingBackgroundVisual(animate: false);
            UpdateAdjacentPreviewSnapshots();

            await HideFirstRunSetupAsync();
        }
        catch (Exception ex)
        {
            App.LogException(ex, "MainWindow.FirstRun.Finish");
            _firstRunBusy = false;
            UpdateFirstRunFooter();
            System.Windows.MessageBox.Show(this, "Setup could not be saved. Please try again.", "DashX360", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task HideFirstRunSetupAsync()
    {
        if (_firstRunLayer == null || _firstRunFooterLayer == null || _firstRunHeaderBlocker == null)
        {
            return;
        }

        _audioService.Play("select");
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var setupFade = new DoubleAnimation(_firstRunLayer.Opacity, 0, TimeSpan.FromMilliseconds(330)) { EasingFunction = easing };
        var footerFade = new DoubleAnimation(_firstRunFooterLayer.Opacity, 0, TimeSpan.FromMilliseconds(250)) { EasingFunction = easing };
        var homeFade = new DoubleAnimation(ContentHost.Opacity, 1, TimeSpan.FromMilliseconds(420)) { EasingFunction = easing };
        _firstRunLayer.BeginAnimation(OpacityProperty, setupFade);
        _firstRunFooterLayer.BeginAnimation(OpacityProperty, footerFade);
        ContentHost.BeginAnimation(OpacityProperty, homeFade);
        await Task.Delay(440);

        _firstRunLayer.Visibility = Visibility.Collapsed;
        _firstRunLayer.IsHitTestVisible = false;
        _firstRunFooterLayer.Visibility = Visibility.Collapsed;
        _firstRunHeaderBlocker.Visibility = Visibility.Collapsed;
        ContentHost.BeginAnimation(OpacityProperty, null);
        ContentHost.Opacity = 1;
        ContentHost.IsHitTestVisible = true;

        _firstRunSetupActive = false;
        _firstRunBusy = false;
        _isMenuFakeLoadingActive = false;
        _signInToastSequenceActive = false;
        _firstRunController?.Dispose();
        _firstRunController = null;

        ScheduleStartupPrewarm();
        QueueFocusFirstButton();
        _ = RunSignInToastSequenceAsync();
    }

    private void FirstRunPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_firstRunSetupActive || _firstRunBusy)
        {
            return;
        }

        DashboardInputAction? action = e.Key switch
        {
            Key.Up => DashboardInputAction.MoveUp,
            Key.Down => DashboardInputAction.MoveDown,
            Key.Left => DashboardInputAction.MoveLeft,
            Key.Right => DashboardInputAction.MoveRight,
            Key.Enter => DashboardInputAction.Activate,
            Key.Space => DashboardInputAction.Activate,
            Key.Escape => DashboardInputAction.Back,
            Key.Back => DashboardInputAction.Back,
            _ => null
        };

        if (action.HasValue)
        {
            e.Handled = true;
            HandleFirstRunControllerAction(action.Value);
        }
    }

    private void HandleFirstRunControllerAction(DashboardInputAction action)
    {
        if (!_firstRunSetupActive || _firstRunBusy)
        {
            return;
        }

        switch (action)
        {
            case DashboardInputAction.MoveUp:
                MoveFirstRunFocus(FocusNavigationDirection.Up);
                break;
            case DashboardInputAction.MoveDown:
                MoveFirstRunFocus(FocusNavigationDirection.Down);
                break;
            case DashboardInputAction.MoveLeft:
                MoveFirstRunFocus(FocusNavigationDirection.Left);
                break;
            case DashboardInputAction.MoveRight:
                MoveFirstRunFocus(FocusNavigationDirection.Right);
                break;
            case DashboardInputAction.Back:
                if (_firstRunStep > 0)
                {
                    _audioService.Play("menu-out");
                    RenderFirstRunStep(_firstRunStep - 1, -1);
                }
                break;
            case DashboardInputAction.Activate:
                ActivateFirstRunFocus();
                break;
        }
    }

    private void MoveFirstRunFocus(FocusNavigationDirection direction)
    {
        if (Keyboard.FocusedElement is UIElement focused)
        {
            if (!focused.MoveFocus(new TraversalRequest(direction)))
            {
                _firstRunPreferredFocus?.Focus();
            }
        }
        else
        {
            _firstRunPreferredFocus?.Focus();
        }
    }

    private void ActivateFirstRunFocus()
    {
        if (Keyboard.FocusedElement is Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            return;
        }

        if (_firstRunStep == 3)
        {
            _ = GoFirstRunNextAsync();
        }
    }

    private void SetFirstRunHeading(string step, string title, string description)
    {
        if (_firstRunStepText != null) _firstRunStepText.Text = step;
        if (_firstRunSectionTitle != null) _firstRunSectionTitle.Text = title;
        if (_firstRunSectionDescription != null) _firstRunSectionDescription.Text = description;
    }

    private void UpdateFirstRunProgress()
    {
        if (_firstRunProgressSegments == null)
        {
            return;
        }

        _firstRunProgressSegments.Children.Clear();
        Brush green = FindSetupBrush("MetroGreenBrush", Brushes.Green);
        for (int i = 1; i <= 6; i++)
        {
            _firstRunProgressSegments.Children.Add(new Border
            {
                Width = 34,
                Height = 5,
                Margin = new Thickness(i == 1 ? 0 : 5, 0, 0, 0),
                Background = i <= _firstRunStep ? green : new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)),
                CornerRadius = new CornerRadius(1)
            });
        }
    }

    private void UpdateFirstRunFooter()
    {
        if (_firstRunAcceptHint != null)
        {
            _firstRunAcceptHint.Text = _firstRunBusy ? "Working..." : _firstRunStep switch
            {
                0 => "Begin",
                5 => "Choose",
                6 => "Enter dashboard",
                _ => "Select / Continue"
            };
        }

        Visibility backVisibility = _firstRunStep == 0 || _firstRunBusy ? Visibility.Collapsed : Visibility.Visible;
        if (_firstRunBackBadge != null) _firstRunBackBadge.Visibility = backVisibility;
        if (_firstRunBackHint != null) _firstRunBackHint.Visibility = backVisibility;
    }

    private Button CreateSetupButton(string text, bool primary)
    {
        Brush green = FindSetupBrush("MetroGreenBrush", new SolidColorBrush(Color.FromRgb(2, 141, 2)));
        var button = new Button
        {
            Content = text,
            Height = 45,
            Margin = new Thickness(0, 0, 0, 7),
            Padding = new Thickness(16, 0, 16, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 19,
            FontWeight = FontWeights.Light,
            Background = primary ? green : new SolidColorBrush(Color.FromArgb(218, 255, 255, 255)),
            Foreground = primary ? Brushes.White : new SolidColorBrush(Color.FromRgb(32, 36, 38)),
            BorderBrush = primary ? Brushes.White : new SolidColorBrush(Color.FromArgb(100, 255, 255, 255)),
            BorderThickness = new Thickness(primary ? 2 : 1),
            FocusVisualStyle = null
        };

        button.GotKeyboardFocus += (_, _) => ApplySetupButtonFocus(button, true);
        button.LostKeyboardFocus += (_, _) => ApplySetupButtonFocus(button, false, primary);
        button.MouseEnter += (_, _) => ApplySetupButtonFocus(button, true);
        button.MouseLeave += (_, _) =>
        {
            if (!button.IsKeyboardFocused) ApplySetupButtonFocus(button, false, primary);
        };
        return button;
    }

    private void ApplySetupButtonFocus(Button button, bool focused, bool primary = false)
    {
        Brush green = FindSetupBrush("MetroGreenBrush", Brushes.Green);
        if (focused)
        {
            button.Background = green;
            button.Foreground = Brushes.White;
            button.BorderBrush = Brushes.White;
            button.BorderThickness = new Thickness(3);
            _audioService.Play("focus");
        }
        else
        {
            button.Background = primary ? green : new SolidColorBrush(Color.FromArgb(218, 255, 255, 255));
            button.Foreground = primary ? Brushes.White : new SolidColorBrush(Color.FromRgb(32, 36, 38));
            button.BorderBrush = primary ? Brushes.White : new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
            button.BorderThickness = new Thickness(primary ? 2 : 1);
        }
    }

    private static Border CreatePromptBadge(string text, Brush background)
    {
        return new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = background,
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
    }

    private static TextBlock CreateSystemLabel(string text, Thickness? margin = null)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(102, 108, 110)),
            Margin = margin ?? new Thickness(0, 0, 0, 3)
        };
    }

    private static TextBlock CreateSystemValue(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 23,
            FontWeight = FontWeights.Light,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 34, 36))
        };
    }

    private Brush FindSetupBrush(string key, Brush fallback)
    {
        return TryFindResource(key) as Brush ?? fallback;
    }

    private static string NormalizeFirstRunGamertag(string? value)
    {
        string text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Player";
        }
        return text.Length <= 15 ? text : text[..15];
    }

    private void DisposeFirstRunSetup()
    {
        try
        {
            _firstRunReadyTimer?.Stop();
            _firstRunLifetime.Cancel();
            _firstRunController?.Dispose();
            _firstRunController = null;
        }
        catch
        {
        }
    }
}
