using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using XboxMetroLauncher.Input;
using XboxMetroLauncher.Utilities;

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
                    new GradientStop(Color.FromRgb(69, 71, 72), 0.00),
                    new GradientStop(Color.FromRgb(91, 93, 94), 0.16),
                    new GradientStop(Color.FromRgb(136, 138, 139), 0.35),
                    new GradientStop(Color.FromRgb(196, 197, 197), 0.56),
                    new GradientStop(Color.FromRgb(226, 227, 226), 0.78),
                    new GradientStop(Color.FromRgb(239, 239, 238), 1.00)
                }
            }
        };
        _profileSigninLayer = root;
        Panel.SetZIndex(root, 5000);

        // The original sign-in view has a very soft horizontal light sweep through
        // the profile row, not a card or dashboard panel.
        var sweep = new Rectangle
        {
            Height = 250,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 72, 0, 0),
            IsHitTestVisible = false,
            Opacity = 0.38,
            Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.00),
                    new GradientStop(Color.FromArgb(46, 255, 255, 255), 0.24),
                    new GradientStop(Color.FromArgb(28, 255, 255, 255), 0.68),
                    new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.00)
                }
            }
        };
        root.Children.Add(sweep);

        var title = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 43, 90, 0)
        };
        title.Children.Add(new TextBlock
        {
            Text = "sign in or out",
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Light"),
            FontSize = 30,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        title.Children.Add(new TextBlock
        {
            Text = "Choose your profile",
            Foreground = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            Margin = new Thickness(0, -1, 0, 0),
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right
        });
        root.Children.Add(title);

        var profileCanvas = new Canvas
        {
            Width = 1140,
            Height = 390,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 54, 0, 0)
        };
        root.Children.Add(profileCanvas);

        var current = CreateCurrentProfileCard();
        Canvas.SetLeft(current, 295);
        Canvas.SetTop(current, 54);
        profileCanvas.Children.Add(current);
        _profileSigninPrimaryButton = current;

        var create = CreateCreateProfileCard();
        Canvas.SetLeft(create, 622);
        Canvas.SetTop(create, 79);
        profileCanvas.Children.Add(create);
        _profileSigninCreateButton = create;

        var ghost = CreateGhostProfileVisual();
        Canvas.SetLeft(ghost, 875);
        Canvas.SetTop(ghost, 91);
        profileCanvas.Children.Add(ghost);

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(84, 0, 0, 43)
        };
        footer.Children.Add(CreateSigninPromptBadge("A", Color.FromRgb(77, 184, 47)));
        footer.Children.Add(new TextBlock
        {
            Text = "Select",
            Foreground = new SolidColorBrush(Color.FromRgb(76, 76, 76)),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 17, 0)
        });
        footer.Children.Add(CreateSigninPromptBadge("B", Color.FromRgb(204, 55, 47)));
        footer.Children.Add(new TextBlock
        {
            Text = "Back",
            Foreground = new SolidColorBrush(Color.FromRgb(76, 76, 76)),
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 0, 0)
        });
        root.Children.Add(footer);

        var tip = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 90, 43)
        };
        tip.Children.Add(new TextBlock
        {
            Text = "Tip: choose a profile",
            Foreground = new SolidColorBrush(Color.FromRgb(103, 103, 103)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        tip.Children.Add(CreateMicrophoneGlyph());
        root.Children.Add(tip);

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
        var button = CreateSigninCardButton(300, 282);
        var canvas = new Canvas { Width = 300, Height = 282 };

        UIElement avatar = BuildSigninAvatar(false);
        Canvas.SetLeft(avatar, 4);
        Canvas.SetTop(avatar, 1);
        canvas.Children.Add(avatar);

        var pictureBorder = new Border
        {
            Width = 32,
            Height = 32,
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Background = Brushes.Black,
            ClipToBounds = true
        };
        _profileSigninGamerPicture = new Image { Stretch = Stretch.UniformToFill };
        pictureBorder.Child = _profileSigninGamerPicture;
        Canvas.SetLeft(pictureBorder, 151);
        Canvas.SetTop(pictureBorder, 98);
        canvas.Children.Add(pictureBorder);

        _profileSigninGamertag = new TextBlock
        {
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Light"),
            FontSize = 17
        };
        Canvas.SetLeft(_profileSigninGamertag, 151);
        Canvas.SetTop(_profileSigninGamertag, 134);
        canvas.Children.Add(_profileSigninGamertag);

        _profileSigninScore = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            LineHeight = 19
        };
        Canvas.SetLeft(_profileSigninScore, 151);
        Canvas.SetTop(_profileSigninScore, 178);
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
        var button = CreateSigninCardButton(235, 250);
        var canvas = new Canvas { Width = 235, Height = 250 };
        UIElement avatar = BuildSigninAvatar(true);
        avatar.RenderTransformOrigin = new Point(0.5, 1.0);
        avatar.RenderTransform = new ScaleTransform(0.88, 0.88);
        Canvas.SetLeft(avatar, -4);
        Canvas.SetTop(avatar, 16);
        canvas.Children.Add(avatar);

        var plus = new TextBlock
        {
            Text = "+",
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Light"),
            FontSize = 36
        };
        Canvas.SetLeft(plus, 125);
        Canvas.SetTop(plus, 78);
        canvas.Children.Add(plus);

        var create = new TextBlock
        {
            Text = "Create\nProfile",
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Light"),
            FontSize = 25,
            LineHeight = 28
        };
        Canvas.SetLeft(create, 125);
        Canvas.SetTop(create, 112);
        canvas.Children.Add(create);

        var caption = new TextBlock
        {
            Text = "Want to make a new\nprofile?",
            Foreground = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            LineHeight = 16
        };
        Canvas.SetLeft(caption, 125);
        Canvas.SetTop(caption, 174);
        canvas.Children.Add(caption);

        button.Content = canvas;
        button.Click += (_, _) => ShowLegacyProfileEditorFromSignin();
        return button;
    }

    private FrameworkElement CreateGhostProfileVisual()
    {
        var grid = new Grid
        {
            Width = 170,
            Height = 238,
            Opacity = 0.28,
            IsHitTestVisible = false
        };
        FrameworkElement avatar = BuildSigninAvatar(true);
        avatar.RenderTransformOrigin = new Point(0.5, 1.0);
        avatar.RenderTransform = new ScaleTransform(0.92, 0.92);
        grid.Children.Add(avatar);
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
            RenderTransformOrigin = new Point(0.5, 0.62),
            RenderTransform = new ScaleTransform(0.90, 0.90),
            Opacity = 0.62
        };

        button.GotKeyboardFocus += (_, _) => AnimateSigninCard(button, 1.06, 1.0);
        button.LostKeyboardFocus += (_, _) => AnimateSigninCard(button, 0.90, 0.62);
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
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(scale, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease });
        button.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, TimeSpan.FromMilliseconds(120)));
    }

    private static FrameworkElement BuildSigninAvatar(bool ghost)
    {
        var canvas = new Canvas { Width = 150, Height = 240, IsHitTestVisible = false };

        Color skinLight = ghost ? Color.FromRgb(232, 233, 233) : Color.FromRgb(238, 193, 153);
        Color skinDark = ghost ? Color.FromRgb(190, 192, 192) : Color.FromRgb(190, 132, 96);
        Color clothLight = ghost ? Color.FromRgb(229, 230, 230) : Color.FromRgb(252, 252, 248);
        Color clothDark = ghost ? Color.FromRgb(194, 196, 196) : Color.FromRgb(205, 207, 200);
        Color pantsLight = ghost ? Color.FromRgb(200, 201, 201) : Color.FromRgb(85, 82, 70);
        Color pantsDark = ghost ? Color.FromRgb(171, 173, 173) : Color.FromRgb(53, 51, 45);
        Color accentColor = ghost ? Color.FromRgb(199, 201, 201) : Color.FromRgb(99, 181, 55);

        var shadow = new Ellipse
        {
            Width = 96,
            Height = 17,
            Fill = new RadialGradientBrush(
                Color.FromArgb(60, 35, 35, 35),
                Color.FromArgb(0, 35, 35, 35))
        };
        Canvas.SetLeft(shadow, 27);
        Canvas.SetTop(shadow, 216);
        canvas.Children.Add(shadow);

        var neck = new Border
        {
            Width = 20,
            Height = 18,
            CornerRadius = new CornerRadius(8),
            Background = MakeGradient(skinLight, skinDark)
        };
        Canvas.SetLeft(neck, 65);
        Canvas.SetTop(neck, 54);
        canvas.Children.Add(neck);

        var torso = new Border
        {
            Width = 76,
            Height = 82,
            CornerRadius = new CornerRadius(22, 22, 12, 12),
            Background = MakeGradient(clothLight, clothDark),
            BorderBrush = new SolidColorBrush(Color.FromArgb(30, 0, 0, 0)),
            BorderThickness = new Thickness(1)
        };
        Canvas.SetLeft(torso, 37);
        Canvas.SetTop(torso, 66);
        canvas.Children.Add(torso);

        var leftArm = CreateAvatarLimb(18, 77, skinLight, skinDark, 10);
        leftArm.RenderTransform = new RotateTransform(11, 9, 0);
        Canvas.SetLeft(leftArm, 25);
        Canvas.SetTop(leftArm, 72);
        canvas.Children.Add(leftArm);

        var rightArm = CreateAvatarLimb(18, 77, skinLight, skinDark, 10);
        rightArm.RenderTransform = new RotateTransform(-11, 9, 0);
        Canvas.SetLeft(rightArm, 108);
        Canvas.SetTop(rightArm, 72);
        canvas.Children.Add(rightArm);

        var leftLeg = CreateAvatarLimb(29, 78, pantsLight, pantsDark, 9);
        Canvas.SetLeft(leftLeg, 43);
        Canvas.SetTop(leftLeg, 139);
        canvas.Children.Add(leftLeg);

        var rightLeg = CreateAvatarLimb(29, 78, pantsLight, pantsDark, 9);
        Canvas.SetLeft(rightLeg, 78);
        Canvas.SetTop(rightLeg, 139);
        canvas.Children.Add(rightLeg);

        var leftShoe = new Ellipse { Width = 38, Height = 17, Fill = MakeGradient(pantsLight, Color.FromRgb(38, 38, 37)) };
        Canvas.SetLeft(leftShoe, 36);
        Canvas.SetTop(leftShoe, 207);
        canvas.Children.Add(leftShoe);
        var rightShoe = new Ellipse { Width = 38, Height = 17, Fill = MakeGradient(pantsLight, Color.FromRgb(38, 38, 37)) };
        Canvas.SetLeft(rightShoe, 76);
        Canvas.SetTop(rightShoe, 207);
        canvas.Children.Add(rightShoe);

        var head = new Ellipse
        {
            Width = 51,
            Height = 57,
            Fill = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.34, 0.28),
                Center = new Point(0.45, 0.42),
                RadiusX = 0.72,
                RadiusY = 0.72,
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(skinLight, 0),
                    new GradientStop(skinDark, 1)
                }
            },
            Stroke = new SolidColorBrush(Color.FromArgb(32, 0, 0, 0)),
            StrokeThickness = 1
        };
        Canvas.SetLeft(head, 50);
        Canvas.SetTop(head, 7);
        canvas.Children.Add(head);

        var leftEar = new Ellipse { Width = 7, Height = 13, Fill = MakeGradient(skinLight, skinDark) };
        Canvas.SetLeft(leftEar, 47);
        Canvas.SetTop(leftEar, 29);
        canvas.Children.Add(leftEar);
        var rightEar = new Ellipse { Width = 7, Height = 13, Fill = MakeGradient(skinLight, skinDark) };
        Canvas.SetLeft(rightEar, 97);
        Canvas.SetTop(rightEar, 29);
        canvas.Children.Add(rightEar);

        if (!ghost)
        {
            var hair = new Border
            {
                Width = 44,
                Height = 16,
                CornerRadius = new CornerRadius(14, 14, 6, 6),
                Background = MakeGradient(Color.FromRgb(90, 63, 43), Color.FromRgb(45, 32, 24))
            };
            Canvas.SetLeft(hair, 54);
            Canvas.SetTop(hair, 8);
            canvas.Children.Add(hair);

            var leftEye = new Ellipse { Width = 5, Height = 3, Fill = new SolidColorBrush(Color.FromRgb(42, 42, 42)) };
            Canvas.SetLeft(leftEye, 63);
            Canvas.SetTop(leftEye, 34);
            canvas.Children.Add(leftEye);
            var rightEye = new Ellipse { Width = 5, Height = 3, Fill = new SolidColorBrush(Color.FromRgb(42, 42, 42)) };
            Canvas.SetLeft(rightEye, 83);
            Canvas.SetTop(rightEye, 34);
            canvas.Children.Add(rightEye);

            var mouth = new Border
            {
                Width = 14,
                Height = 2,
                CornerRadius = new CornerRadius(1),
                Background = new SolidColorBrush(Color.FromArgb(120, 100, 55, 48))
            };
            Canvas.SetLeft(mouth, 69);
            Canvas.SetTop(mouth, 49);
            canvas.Children.Add(mouth);
        }

        var chestStripe = new Border
        {
            Width = 49,
            Height = 18,
            Background = new SolidColorBrush(accentColor),
            CornerRadius = new CornerRadius(9),
            Opacity = ghost ? 0.52 : 0.96
        };
        Canvas.SetLeft(chestStripe, 51);
        Canvas.SetTop(chestStripe, 84);
        canvas.Children.Add(chestStripe);

        if (!ghost)
        {
            var xbox = new TextBlock
            {
                Text = "XBOX",
                Foreground = new SolidColorBrush(accentColor),
                FontWeight = FontWeights.Bold,
                FontSize = 11
            };
            Canvas.SetLeft(xbox, 58);
            Canvas.SetTop(xbox, 106);
            canvas.Children.Add(xbox);
        }

        return canvas;
    }

    private static Border CreateAvatarLimb(double width, double height, Color light, Color dark, double radius)
    {
        return new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(radius),
            Background = MakeGradient(light, dark),
            BorderBrush = new SolidColorBrush(Color.FromArgb(18, 0, 0, 0)),
            BorderThickness = new Thickness(1)
        };
    }

    private static Brush MakeGradient(Color light, Color dark)
    {
        return new LinearGradientBrush(light, dark, new Point(0.2, 0), new Point(0.8, 1));
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

    private static FrameworkElement CreateMicrophoneGlyph()
    {
        var canvas = new Canvas
        {
            Width = 16,
            Height = 18,
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        var stroke = new SolidColorBrush(Color.FromRgb(102, 102, 102));
        var capsule = new Border
        {
            Width = 7,
            Height = 11,
            CornerRadius = new CornerRadius(4),
            BorderBrush = stroke,
            BorderThickness = new Thickness(1.3)
        };
        Canvas.SetLeft(capsule, 4.5);
        Canvas.SetTop(capsule, 0);
        canvas.Children.Add(capsule);
        var stem = new Border { Width = 1.3, Height = 5, Background = stroke };
        Canvas.SetLeft(stem, 7.4);
        Canvas.SetTop(stem, 10.5);
        canvas.Children.Add(stem);
        var foot = new Border { Width = 7, Height = 1.3, Background = stroke };
        Canvas.SetLeft(foot, 4.5);
        Canvas.SetTop(foot, 15);
        canvas.Children.Add(foot);
        return canvas;
    }

    private void RefreshProfileSigninData()
    {
        if (_profileSigninGamertag != null)
        {
            _profileSigninGamertag.Text = string.IsNullOrWhiteSpace(_viewModel.Profile.Gamertag) ? "Player" : _viewModel.Profile.Gamertag;
        }
        if (_profileSigninScore != null)
        {
            _profileSigninScore.Text = $"{_viewModel.Profile.Gamerscore.ToString("N0", CultureInfo.InvariantCulture)} G\nHard Drive";
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
