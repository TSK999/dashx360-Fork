using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using XboxMetroLauncher.Input;

namespace XboxMetroLauncher;

public partial class MainWindow
{
    private bool _profileSigninInstalled;
    private bool _profileSigninShowingLegacyEditor;
    private bool _profileSigninControllerCaptured;
    private bool _profileSigninPreviousControllerInput;
    private Grid? _profileSigninLayer;
    private Button? _profileSigninPrimaryButton;
    private Button? _profileSigninCreateButton;
    private TextBlock? _profileSigninGamertag;
    private TextBlock? _profileSigninScore;
    private Image? _profileSigninGamerPicture;
    private ControllerInputService? _profileSigninController;
    private readonly List<UIElement> _legacyProfileEditorChildren = new();

    internal void InstallProfileSigninScreen()
    {
        if (_profileSigninInstalled)
        {
            return;
        }

        _profileSigninInstalled = true;
        BuildProfileSigninScreen();
        _viewModel.PropertyChanged += ProfileSigninOnViewModelPropertyChanged;
        _profileSigninController = new ControllerInputService(
            HandleProfileSigninControllerAction,
            () => _viewModel.IsProfileEditorOpen && !_profileSigninShowingLegacyEditor && IsVisible && IsActive);
        _profileSigninController.Start();
        Closed += (_, _) =>
        {
            _viewModel.PropertyChanged -= ProfileSigninOnViewModelPropertyChanged;
            RestoreProfileSigninControllerCapture();
            _profileSigninController?.Dispose();
            _profileSigninController = null;
        };
    }

    private void BuildProfileSigninScreen()
    {
        if (_profileSigninLayer != null)
        {
            return;
        }

        _legacyProfileEditorChildren.Clear();
        foreach (UIElement child in ProfileEditorOverlay.Children.Cast<UIElement>().ToList())
        {
            _legacyProfileEditorChildren.Add(child);
            child.Visibility = Visibility.Collapsed;
        }

        var root = new Grid
        {
            Width = 1280,
            Height = 720,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Focusable = false,
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(70, 72, 73), 0.00),
                    new GradientStop(Color.FromRgb(101, 103, 104), 0.18),
                    new GradientStop(Color.FromRgb(205, 206, 206), 0.54),
                    new GradientStop(Color.FromRgb(232, 232, 231), 1.00)
                }
            }
        };
        _profileSigninLayer = root;
        Panel.SetZIndex(root, 5000);

        root.Children.Add(new Rectangle
        {
            Height = 188,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = new LinearGradientBrush(
                Color.FromArgb(22, 255, 255, 255),
                Color.FromArgb(2, 255, 255, 255),
                new Point(0, 0),
                new Point(0, 1)),
            IsHitTestVisible = false
        });

        var title = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 46, 92, 0)
        };
        title.Children.Add(new TextBlock
        {
            Text = "sign in or out",
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Light"),
            FontSize = 31,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        title.Children.Add(new TextBlock
        {
            Text = "Choose your profile",
            Foreground = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 14,
            Margin = new Thickness(0, -2, 0, 0),
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        root.Children.Add(title);

        var profileCanvas = new Canvas
        {
            Width = 1140,
            Height = 430,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 30, 0, 0)
        };
        root.Children.Add(profileCanvas);

        var current = CreateCurrentProfileCard();
        Canvas.SetLeft(current, 290);
        Canvas.SetTop(current, 68);
        profileCanvas.Children.Add(current);
        _profileSigninPrimaryButton = current;

        var create = CreateCreateProfileCard();
        Canvas.SetLeft(create, 620);
        Canvas.SetTop(create, 92);
        profileCanvas.Children.Add(create);
        _profileSigninCreateButton = create;

        var ghost = CreateGhostProfileVisual();
        Canvas.SetLeft(ghost, 870);
        Canvas.SetTop(ghost, 105);
        profileCanvas.Children.Add(ghost);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(84, 0, 0, 46)
        };
        footer.Children.Add(CreateSigninPromptBadge("A", Color.FromRgb(71, 183, 43)));
        footer.Children.Add(new TextBlock
        {
            Text = "Select",
            Foreground = Brushes.White,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 18, 0)
        });
        footer.Children.Add(CreateSigninPromptBadge("B", Color.FromRgb(205, 54, 45)));
        footer.Children.Add(new TextBlock
        {
            Text = "Back",
            Foreground = Brushes.White,
            FontSize = 15,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0)
        });
        root.Children.Add(footer);

        root.Children.Add(new TextBlock
        {
            Text = "Tip: choose a profile to continue",
            Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 92, 47)
        });

        root.IsVisibleChanged += (_, _) =>
        {
            if (!root.IsVisible || _profileSigninShowingLegacyEditor)
            {
                return;
            }

            RefreshProfileSigninData();
            root.Opacity = 0;
            root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            Dispatcher.BeginInvoke((Action)(() => _profileSigninPrimaryButton?.Focus()), DispatcherPriority.Input);
        };

        ProfileEditorOverlay.Children.Add(root);
        RefreshProfileSigninData();
    }

    private Button CreateCurrentProfileCard()
    {
        var button = CreateSigninCardButton(315, 286);
        var canvas = new Canvas { Width = 315, Height = 286 };

        UIElement avatar = BuildSigninAvatar(false);
        Canvas.SetLeft(avatar, 12);
        Canvas.SetTop(avatar, 4);
        canvas.Children.Add(avatar);

        var pictureBorder = new Border
        {
            Width = 34,
            Height = 34,
            BorderBrush = new SolidColorBrush(Color.FromArgb(130, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Background = Brushes.Black,
            ClipToBounds = true
        };
        _profileSigninGamerPicture = new Image { Stretch = Stretch.UniformToFill };
        pictureBorder.Child = _profileSigninGamerPicture;
        Canvas.SetLeft(pictureBorder, 150);
        Canvas.SetTop(pictureBorder, 106);
        canvas.Children.Add(pictureBorder);

        _profileSigninGamertag = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Light"),
            FontSize = 18
        };
        Canvas.SetLeft(_profileSigninGamertag, 150);
        Canvas.SetTop(_profileSigninGamertag, 142);
        canvas.Children.Add(_profileSigninGamertag);

        _profileSigninScore = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255)),
            FontSize = 14,
            LineHeight = 20
        };
        Canvas.SetLeft(_profileSigninScore, 150);
        Canvas.SetTop(_profileSigninScore, 184);
        canvas.Children.Add(_profileSigninScore);

        button.Content = canvas;
        button.Click += (_, _) =>
        {
            if (_viewModel.CloseProfileEditorCommand.CanExecute(null))
            {
                _viewModel.CloseProfileEditorCommand.Execute(null);
            }
        };
        return button;
    }

    private Button CreateCreateProfileCard()
    {
        var button = CreateSigninCardButton(240, 250);
        var canvas = new Canvas { Width = 240, Height = 250 };
        UIElement avatar = BuildSigninAvatar(true);
        Canvas.SetLeft(avatar, 4);
        Canvas.SetTop(avatar, 2);
        canvas.Children.Add(avatar);

        var plus = new TextBlock
        {
            Text = "+",
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Light"),
            FontSize = 42
        };
        Canvas.SetLeft(plus, 132);
        Canvas.SetTop(plus, 92);
        canvas.Children.Add(plus);

        var create = new TextBlock
        {
            Text = "Create\nProfile",
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Light"),
            FontSize = 27,
            LineHeight = 31
        };
        Canvas.SetLeft(create, 132);
        Canvas.SetTop(create, 128);
        canvas.Children.Add(create);

        var caption = new TextBlock
        {
            Text = "Want to make a new\nprofile?",
            Foreground = new SolidColorBrush(Color.FromArgb(225, 255, 255, 255)),
            FontSize = 13,
            LineHeight = 17
        };
        Canvas.SetLeft(caption, 132);
        Canvas.SetTop(caption, 196);
        canvas.Children.Add(caption);

        button.Content = canvas;
        button.Click += (_, _) => ShowLegacyProfileEditorFromSignin();
        return button;
    }

    private FrameworkElement CreateGhostProfileVisual()
    {
        var grid = new Grid { Width = 180, Height = 230, Opacity = 0.34, IsHitTestVisible = false };
        grid.Children.Add(BuildSigninAvatar(true));
        return grid;
    }

    private Button CreateSigninCardButton(double width, double height)
    {
        var button = new Button
        {
            Width = width,
            Height = height,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FocusVisualStyle = null,
            Tag = "ProfileMenuOption",
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.92, 0.92),
            Opacity = 0.72
        };

        button.GotKeyboardFocus += (_, _) => AnimateSigninCard(button, 1.08, 1.0);
        button.LostKeyboardFocus += (_, _) => AnimateSigninCard(button, 0.92, 0.72);
        button.MouseEnter += (_, _) =>
        {
            if (button.IsEnabled)
            {
                button.Focus();
            }
        };
        return button;
    }

    private static void AnimateSigninCard(Button button, double scale, double opacity)
    {
        if (button.RenderTransform is not ScaleTransform transform)
        {
            transform = new ScaleTransform(1, 1);
            button.RenderTransform = transform;
        }

        var ease = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(145)) { EasingFunction = ease });
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(145)) { EasingFunction = ease });
        button.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(120)));
    }

    private static FrameworkElement BuildSigninAvatar(bool ghost)
    {
        var canvas = new Canvas { Width = 135, Height = 230, IsHitTestVisible = false };
        Brush skin = ghost ? new SolidColorBrush(Color.FromRgb(222, 223, 223)) : new SolidColorBrush(Color.FromRgb(218, 171, 131));
        Brush shirt = ghost ? new SolidColorBrush(Color.FromRgb(222, 223, 223)) : Brushes.WhiteSmoke;
        Brush pants = ghost ? new SolidColorBrush(Color.FromRgb(190, 191, 191)) : new SolidColorBrush(Color.FromRgb(69, 67, 57));
        Brush accent = ghost ? new SolidColorBrush(Color.FromRgb(201, 202, 202)) : new SolidColorBrush(Color.FromRgb(108, 185, 58));

        var shadow = new Ellipse { Width = 90, Height = 18, Fill = new SolidColorBrush(Color.FromArgb(35, 0, 0, 0)) };
        Canvas.SetLeft(shadow, 22); Canvas.SetTop(shadow, 207); canvas.Children.Add(shadow);

        var head = new Ellipse { Width = 45, Height = 51, Fill = skin, Stroke = new SolidColorBrush(Color.FromArgb(36, 0, 0, 0)), StrokeThickness = 1 };
        Canvas.SetLeft(head, 45); Canvas.SetTop(head, 8); canvas.Children.Add(head);

        var torso = new Border { Width = 68, Height = 78, Background = shirt, CornerRadius = new CornerRadius(18, 18, 10, 10) };
        Canvas.SetLeft(torso, 33); Canvas.SetTop(torso, 58); canvas.Children.Add(torso);

        var chest = new Border { Width = 42, Height = 18, Background = accent, CornerRadius = new CornerRadius(8), Opacity = ghost ? 0.55 : 0.95 };
        Canvas.SetLeft(chest, 46); Canvas.SetTop(chest, 75); canvas.Children.Add(chest);

        var armLeft = new Border { Width = 17, Height = 74, Background = skin, CornerRadius = new CornerRadius(9), RenderTransform = new RotateTransform(12), RenderTransformOrigin = new Point(0.5, 0) };
        Canvas.SetLeft(armLeft, 22); Canvas.SetTop(armLeft, 67); canvas.Children.Add(armLeft);
        var armRight = new Border { Width = 17, Height = 74, Background = skin, CornerRadius = new CornerRadius(9), RenderTransform = new RotateTransform(-12), RenderTransformOrigin = new Point(0.5, 0) };
        Canvas.SetLeft(armRight, 96); Canvas.SetTop(armRight, 67); canvas.Children.Add(armRight);

        var legLeft = new Border { Width = 26, Height = 78, Background = pants, CornerRadius = new CornerRadius(8) };
        Canvas.SetLeft(legLeft, 39); Canvas.SetTop(legLeft, 126); canvas.Children.Add(legLeft);
        var legRight = new Border { Width = 26, Height = 78, Background = pants, CornerRadius = new CornerRadius(8) };
        Canvas.SetLeft(legRight, 70); Canvas.SetTop(legRight, 126); canvas.Children.Add(legRight);

        var shoeLeft = new Ellipse { Width = 34, Height = 15, Fill = ghost ? pants : new SolidColorBrush(Color.FromRgb(45, 45, 43)) };
        Canvas.SetLeft(shoeLeft, 32); Canvas.SetTop(shoeLeft, 198); canvas.Children.Add(shoeLeft);
        var shoeRight = new Ellipse { Width = 34, Height = 15, Fill = ghost ? pants : new SolidColorBrush(Color.FromRgb(45, 45, 43)) };
        Canvas.SetLeft(shoeRight, 69); Canvas.SetTop(shoeRight, 198); canvas.Children.Add(shoeRight);

        if (!ghost)
        {
            var xbox = new TextBlock { Text = "XBOX", Foreground = accent, FontWeight = FontWeights.Bold, FontSize = 11 };
            Canvas.SetLeft(xbox, 49); Canvas.SetTop(xbox, 96); canvas.Children.Add(xbox);
        }

        return canvas;
    }

    private static Border CreateSigninPromptBadge(string text, Color color)
    {
        return new Border
        {
            Width = 22,
            Height = 22,
            Background = new SolidColorBrush(color),
            CornerRadius = new CornerRadius(11),
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
    }

    private void RefreshProfileSigninData()
    {
        if (_profileSigninGamertag != null)
        {
            _profileSigninGamertag.Text = string.IsNullOrWhiteSpace(_viewModel.Profile.Gamertag) ? "Player" : _viewModel.Profile.Gamertag;
        }
        if (_profileSigninScore != null)
        {
            _profileSigninScore.Text = $"{_viewModel.Profile.Gamerscore:N0} G\nHard Drive";
        }
        if (_profileSigninGamerPicture != null)
        {
            _profileSigninGamerPicture.Source = LoadProfileSigninImage(_viewModel.Profile.GamerPicturePath);
        }
    }

    private static ImageSource? LoadProfileSigninImage(string? configuredPath)
    {
        try
        {
            string fallback = AppPaths.ResolvePath(System.IO.Path.Combine("Assets", "Profile", "profilepicture.jpg"));
            string candidate = string.IsNullOrWhiteSpace(configuredPath)
                ? fallback
                : (System.IO.Path.IsPathRooted(configuredPath) ? configuredPath : AppPaths.ResolvePath(configuredPath));
            if (!File.Exists(candidate))
            {
                candidate = fallback;
            }
            if (!File.Exists(candidate))
            {
                return null;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(candidate, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private void CaptureProfileSigninController()
    {
        if (_profileSigninControllerCaptured)
        {
            return;
        }

        _profileSigninControllerCaptured = true;
        _profileSigninPreviousControllerInput = _viewModel.Settings.EnableControllerInput;
        if (_profileSigninPreviousControllerInput)
        {
            _viewModel.Settings.EnableControllerInput = false;
        }
    }

    private void RestoreProfileSigninControllerCapture()
    {
        if (!_profileSigninControllerCaptured)
        {
            return;
        }

        _profileSigninControllerCaptured = false;
        if (_profileSigninPreviousControllerInput)
        {
            _viewModel.Settings.EnableControllerInput = true;
        }
        _profileSigninPreviousControllerInput = false;
    }

    private void HandleProfileSigninControllerAction(DashboardInputAction action)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke((Action)(() => HandleProfileSigninControllerAction(action)), DispatcherPriority.Input);
            return;
        }
        if (!_viewModel.IsProfileEditorOpen || _profileSigninShowingLegacyEditor)
        {
            return;
        }

        Button? focused = Keyboard.FocusedElement as Button;
        if (action is DashboardInputAction.MoveLeft or DashboardInputAction.MoveUp)
        {
            (_profileSigninPrimaryButton ?? focused)?.Focus();
            return;
        }
        if (action is DashboardInputAction.MoveRight or DashboardInputAction.MoveDown)
        {
            (_profileSigninCreateButton ?? focused)?.Focus();
            return;
        }
        if (action == DashboardInputAction.Activate)
        {
            (focused ?? _profileSigninPrimaryButton)?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return;
        }
        if (action == DashboardInputAction.Back && _viewModel.CloseProfileEditorCommand.CanExecute(null))
        {
            _viewModel.CloseProfileEditorCommand.Execute(null);
        }
    }

    private void ShowLegacyProfileEditorFromSignin()
    {
        _profileSigninShowingLegacyEditor = true;
        RestoreProfileSigninControllerCapture();
        if (_profileSigninLayer != null)
        {
            _profileSigninLayer.Visibility = Visibility.Collapsed;
        }
        foreach (UIElement child in _legacyProfileEditorChildren)
        {
            child.Visibility = Visibility.Visible;
        }
        if (_viewModel.ToggleProfileEditCommand.CanExecute(null))
        {
            _viewModel.ToggleProfileEditCommand.Execute(null);
        }
        Dispatcher.BeginInvoke((Action)(() => ProfileMenuEditButton?.Focus()), DispatcherPriority.Input);
    }

    private void ResetProfileSigninScreen()
    {
        _profileSigninShowingLegacyEditor = false;
        foreach (UIElement child in _legacyProfileEditorChildren)
        {
            child.Visibility = Visibility.Collapsed;
        }
        if (_profileSigninLayer != null)
        {
            _profileSigninLayer.Visibility = Visibility.Visible;
        }
    }

    private void ProfileSigninOnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, "Profile", StringComparison.Ordinal))
        {
            RefreshProfileSigninData();
            return;
        }
        if (!string.Equals(e.PropertyName, "IsProfileEditorOpen", StringComparison.Ordinal))
        {
            return;
        }

        if (_viewModel.IsProfileEditorOpen)
        {
            if (!_profileSigninShowingLegacyEditor)
            {
                ResetProfileSigninScreen();
                CaptureProfileSigninController();
                RefreshProfileSigninData();
                Dispatcher.BeginInvoke((Action)(() => _profileSigninPrimaryButton?.Focus()), DispatcherPriority.Input);
            }
        }
        else
        {
            RestoreProfileSigninControllerCapture();
            ResetProfileSigninScreen();
        }
    }
}
