using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using XboxMetroLauncher.Input;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Services;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher;

public partial class MainWindow
{
    private bool _profileSigninInstalled;
    private bool _profileSigninControllerCaptured;
    private bool _profileSigninPreviousControllerInput;
    private bool _profileSigninCreating;
    private Grid? _profileSigninLayer;
    private Canvas? _profileSigninCanvas;
    private StackPanel? _profileSigninTitlePanel;
    private TextBlock? _profileSigninTitle;
    private TextBlock? _profileSigninSubtitle;
    private Grid? _profileSigninCreatePanel;
    private TextBox? _profileSigninCreateGamertag;
    private TextBox? _profileSigninCreateName;
    private TextBlock? _profileSigninCreateError;
    private Button? _profileSigninCreateConfirmButton;
    private Button? _profileSigninCreateButton;
    private Button? _profileSigninPrimaryButton;
    private ControllerInputService? _profileSigninController;
    private LocalProfileCatalogService? _profileCatalogService;
    private readonly List<Profile> _profileSigninProfiles = new();
    private readonly List<Button> _profileSigninProfileButtons = new();
    private readonly List<UIElement> _legacyProfileEditorChildren = new();

    internal void InstallProfileSigninScreen()
    {
        if (_profileSigninInstalled)
        {
            return;
        }

        _profileSigninInstalled = true;
        _profileCatalogService = new LocalProfileCatalogService(new JsonStore(AppPaths.UserDataFolder));
        BuildProfileSigninScreen();
        _viewModel.PropertyChanged += ProfileSigninOnViewModelPropertyChanged;
        _profileSigninController = new ControllerInputService(
            HandleProfileSigninControllerAction,
            () => _viewModel.IsProfileEditorOpen && IsVisible && IsActive);
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
            Background = BuildSigninBackground()
        };
        _profileSigninLayer = root;
        Panel.SetZIndex(root, 5000);

        root.Children.Add(new Rectangle
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
        });

        _profileSigninTitlePanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 43, 90, 0)
        };
        _profileSigninTitle = new TextBlock
        {
            Text = "sign in or out",
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Light"),
            FontSize = 30,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _profileSigninSubtitle = new TextBlock
        {
            Text = "Choose your profile",
            Foreground = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            Margin = new Thickness(0, -1, 0, 0),
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        _profileSigninTitlePanel.Children.Add(_profileSigninTitle);
        _profileSigninTitlePanel.Children.Add(_profileSigninSubtitle);
        root.Children.Add(_profileSigninTitlePanel);

        _profileSigninCanvas = new Canvas
        {
            Width = 1140,
            Height = 390,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 54, 0, 0)
        };
        root.Children.Add(_profileSigninCanvas);

        _profileSigninCreatePanel = BuildCreateProfilePanel();
        root.Children.Add(_profileSigninCreatePanel);

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

        root.PreviewKeyDown += ProfileSigninOnPreviewKeyDown;
        root.IsVisibleChanged += async (_, _) =>
        {
            if (!root.IsVisible)
            {
                return;
            }

            await LoadProfileCatalogAndShowChooserAsync();
            root.Opacity = 0;
            root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        };

        ProfileEditorOverlay.Children.Add(root);
        RebuildProfileCards();
    }

    private static Brush BuildSigninBackground()
    {
        return new LinearGradientBrush
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
        };
    }

    private Grid BuildCreateProfilePanel()
    {
        var panel = new Grid
        {
            Width = 650,
            Height = 350,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 54, 0, 0),
            Visibility = Visibility.Collapsed
        };

        var stack = new StackPanel
        {
            Width = 470,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(stack);

        stack.Children.Add(new TextBlock
        {
            Text = "GAMERTAG",
            Foreground = new SolidColorBrush(Color.FromRgb(92, 92, 92)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        _profileSigninCreateGamertag = CreateProfileTextBox(15);
        stack.Children.Add(_profileSigninCreateGamertag);

        stack.Children.Add(new TextBlock
        {
            Text = "NAME  (OPTIONAL)",
            Foreground = new SolidColorBrush(Color.FromRgb(92, 92, 92)),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 22, 0, 6)
        });
        _profileSigninCreateName = CreateProfileTextBox(32);
        stack.Children.Add(_profileSigninCreateName);

        _profileSigninCreateError = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(165, 37, 32)),
            FontSize = 13,
            Margin = new Thickness(0, 9, 0, 0),
            MinHeight = 20
        };
        stack.Children.Add(_profileSigninCreateError);

        _profileSigninCreateConfirmButton = new Button
        {
            Content = "create profile",
            Width = 250,
            Height = 48,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 14, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(73, 169, 43)),
            Foreground = Brushes.White,
            BorderBrush = Brushes.White,
            BorderThickness = new Thickness(2),
            FontFamily = new FontFamily("Segoe UI Light"),
            FontSize = 20,
            FocusVisualStyle = null
        };
        _profileSigninCreateConfirmButton.Click += async (_, _) => await CreateLocalProfileAsync();
        stack.Children.Add(_profileSigninCreateConfirmButton);

        return panel;
    }

    private static TextBox CreateProfileTextBox(int maxLength)
    {
        return new TextBox
        {
            Height = 48,
            MaxLength = maxLength,
            FontFamily = new FontFamily("Segoe UI Light"),
            FontSize = 22,
            Foreground = new SolidColorBrush(Color.FromRgb(55, 55, 55)),
            Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(135, 135, 135)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 7, 12, 7)
        };
    }

    private async Task LoadProfileCatalogAndShowChooserAsync()
    {
        try
        {
            if (_profileCatalogService == null)
            {
                return;
            }

            List<Profile> profiles = await _profileCatalogService.LoadAsync(_viewModel.Profile);
            _profileSigninProfiles.Clear();
            _profileSigninProfiles.AddRange(profiles);
            ShowProfileChooser();
            RebuildProfileCards();
            Dispatcher.BeginInvoke((Action)(() => _profileSigninPrimaryButton?.Focus()), DispatcherPriority.Input);
        }
        catch (Exception ex)
        {
            App.LogException(ex, "ProfileSignin.LoadCatalog");
            _profileSigninProfiles.Clear();
            _profileSigninProfiles.Add(LocalProfileCatalogService.Clone(_viewModel.Profile));
            ShowProfileChooser();
            RebuildProfileCards();
        }
    }

    private void RebuildProfileCards()
    {
        if (_profileSigninCanvas == null)
        {
            return;
        }

        _profileSigninCanvas.Children.Clear();
        _profileSigninProfileButtons.Clear();
        _profileSigninPrimaryButton = null;

        if (_profileSigninProfiles.Count == 0)
        {
            _profileSigninProfiles.Add(LocalProfileCatalogService.Clone(_viewModel.Profile));
        }

        int visibleProfiles = Math.Min(_profileSigninProfiles.Count, 4);
        double spacing = visibleProfiles <= 2 ? 275 : 235;
        double totalWidth = visibleProfiles * spacing + 235;
        double startX = Math.Max(24, (1140 - totalWidth) / 2);

        for (int index = 0; index < visibleProfiles; index++)
        {
            Profile profile = _profileSigninProfiles[index];
            Button button = CreateProfileCard(profile);
            Canvas.SetLeft(button, startX + index * spacing);
            Canvas.SetTop(button, index == 0 ? 54 : 69);
            _profileSigninCanvas.Children.Add(button);
            _profileSigninProfileButtons.Add(button);

            if (string.Equals(profile.ProfileId, _viewModel.Profile.ProfileId, StringComparison.OrdinalIgnoreCase))
            {
                _profileSigninPrimaryButton = button;
            }
        }

        _profileSigninPrimaryButton ??= _profileSigninProfileButtons.FirstOrDefault();

        _profileSigninCreateButton = CreateCreateProfileCard();
        Canvas.SetLeft(_profileSigninCreateButton, startX + visibleProfiles * spacing);
        Canvas.SetTop(_profileSigninCreateButton, 79);
        _profileSigninCanvas.Children.Add(_profileSigninCreateButton);

        double ghostX = startX + visibleProfiles * spacing + 245;
        if (ghostX < 1000)
        {
            FrameworkElement ghost = CreateGhostProfileVisual();
            Canvas.SetLeft(ghost, ghostX);
            Canvas.SetTop(ghost, 91);
            _profileSigninCanvas.Children.Add(ghost);
        }
    }

    private Button CreateProfileCard(Profile profile)
    {
        var button = CreateSigninCardButton(245, 282);
        var canvas = new Canvas { Width = 245, Height = 282 };

        FrameworkElement avatar = BuildSigninAvatar(false);
        avatar.RenderTransformOrigin = new Point(0.5, 1.0);
        avatar.RenderTransform = new ScaleTransform(0.95, 0.95);
        Canvas.SetLeft(avatar, -2);
        Canvas.SetTop(avatar, 1);
        canvas.Children.Add(avatar);

        var pictureBorder = new Border
        {
            Width = 32,
            Height = 32,
            BorderBrush = new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Background = Brushes.Black,
            ClipToBounds = true,
            Child = new Image
            {
                Stretch = Stretch.UniformToFill,
                Source = LoadProfileSigninImage(profile.GamerPicturePath)
            }
        };
        Canvas.SetLeft(pictureBorder, 142);
        Canvas.SetTop(pictureBorder, 98);
        canvas.Children.Add(pictureBorder);

        var gamertag = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(profile.Gamertag) ? "Player" : profile.Gamertag,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Light"),
            FontSize = 17
        };
        Canvas.SetLeft(gamertag, 142);
        Canvas.SetTop(gamertag, 134);
        canvas.Children.Add(gamertag);

        var score = new TextBlock
        {
            Text = $"{Math.Max(0, profile.Gamerscore).ToString("N0", CultureInfo.GetCultureInfo("en-US"))} G\nHard Drive",
            Foreground = new SolidColorBrush(Color.FromArgb(242, 255, 255, 255)),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 13,
            LineHeight = 19
        };
        Canvas.SetLeft(score, 142);
        Canvas.SetTop(score, 178);
        canvas.Children.Add(score);

        button.Content = canvas;
        button.DataContext = profile;
        button.Click += async (_, _) => await ActivateProfileAsync(profile);
        return button;
    }

    private Button CreateCreateProfileCard()
    {
        var button = CreateSigninCardButton(220, 250);
        var canvas = new Canvas { Width = 220, Height = 250 };
        FrameworkElement avatar = BuildSigninAvatar(true);
        avatar.RenderTransformOrigin = new Point(0.5, 1.0);
        avatar.RenderTransform = new ScaleTransform(0.84, 0.84);
        Canvas.SetLeft(avatar, -10);
        Canvas.SetTop(avatar, 18);
        canvas.Children.Add(avatar);

        var plus = new TextBlock
        {
            Text = "+",
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Segoe UI Light"),
            FontSize = 36
        };
        Canvas.SetLeft(plus, 118);
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
        Canvas.SetLeft(create, 118);
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
        Canvas.SetLeft(caption, 118);
        Canvas.SetTop(caption, 174);
        canvas.Children.Add(caption);

        button.Content = canvas;
        button.Click += (_, _) => ShowCreateProfileForm();
        return button;
    }

    private FrameworkElement CreateGhostProfileVisual()
    {
        var grid = new Grid
        {
            Width = 150,
            Height = 238,
            Opacity = 0.25,
            IsHitTestVisible = false
        };
        FrameworkElement avatar = BuildSigninAvatar(true);
        avatar.RenderTransformOrigin = new Point(0.5, 1.0);
        avatar.RenderTransform = new ScaleTransform(0.88, 0.88);
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
            Fill = new RadialGradientBrush(Color.FromArgb(60, 35, 35, 35), Color.FromArgb(0, 35, 35, 35))
        };
        Canvas.SetLeft(shadow, 27);
        Canvas.SetTop(shadow, 216);
        canvas.Children.Add(shadow);

        var neck = new Border
        {
            Width = 18,
            Height = 19,
            CornerRadius = new CornerRadius(7),
            Background = new LinearGradientBrush(skinLight, skinDark, 90)
        };
        Canvas.SetLeft(neck, 66);
        Canvas.SetTop(neck, 50);
        canvas.Children.Add(neck);

        var head = new Ellipse
        {
            Width = 48,
            Height = 55,
            Fill = new RadialGradientBrush(skinLight, skinDark),
            Stroke = new SolidColorBrush(Color.FromArgb(34, 0, 0, 0)),
            StrokeThickness = 1
        };
        Canvas.SetLeft(head, 51);
        Canvas.SetTop(head, 7);
        canvas.Children.Add(head);

        var hair = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M 54,27 C 55,10 91,4 98,27 C 88,17 68,16 54,27 Z"),
            Fill = ghost ? new SolidColorBrush(Color.FromRgb(185, 187, 187)) : new SolidColorBrush(Color.FromRgb(99, 72, 48)),
            Opacity = ghost ? 0.4 : 0.95
        };
        canvas.Children.Add(hair);

        var torso = new Border
        {
            Width = 70,
            Height = 78,
            Background = new LinearGradientBrush(clothLight, clothDark, 90),
            CornerRadius = new CornerRadius(18, 18, 10, 10)
        };
        Canvas.SetLeft(torso, 40);
        Canvas.SetTop(torso, 61);
        canvas.Children.Add(torso);

        var chest = new Border
        {
            Width = 42,
            Height = 18,
            Background = new SolidColorBrush(accentColor),
            CornerRadius = new CornerRadius(8),
            Opacity = ghost ? 0.48 : 0.95
        };
        Canvas.SetLeft(chest, 54);
        Canvas.SetTop(chest, 77);
        canvas.Children.Add(chest);

        var leftArm = new Border
        {
            Width = 17,
            Height = 76,
            Background = new LinearGradientBrush(skinLight, skinDark, 0),
            CornerRadius = new CornerRadius(9),
            RenderTransform = new RotateTransform(11),
            RenderTransformOrigin = new Point(0.5, 0)
        };
        Canvas.SetLeft(leftArm, 27);
        Canvas.SetTop(leftArm, 68);
        canvas.Children.Add(leftArm);

        var rightArm = new Border
        {
            Width = 17,
            Height = 76,
            Background = new LinearGradientBrush(skinLight, skinDark, 0),
            CornerRadius = new CornerRadius(9),
            RenderTransform = new RotateTransform(-11),
            RenderTransformOrigin = new Point(0.5, 0)
        };
        Canvas.SetLeft(rightArm, 106);
        Canvas.SetTop(rightArm, 68);
        canvas.Children.Add(rightArm);

        var leftLeg = new Border
        {
            Width = 27,
            Height = 80,
            Background = new LinearGradientBrush(pantsLight, pantsDark, 90),
            CornerRadius = new CornerRadius(8)
        };
        Canvas.SetLeft(leftLeg, 45);
        Canvas.SetTop(leftLeg, 131);
        canvas.Children.Add(leftLeg);

        var rightLeg = new Border
        {
            Width = 27,
            Height = 80,
            Background = new LinearGradientBrush(pantsLight, pantsDark, 90),
            CornerRadius = new CornerRadius(8)
        };
        Canvas.SetLeft(rightLeg, 78);
        Canvas.SetTop(rightLeg, 131);
        canvas.Children.Add(rightLeg);

        var shoeBrush = ghost ? new SolidColorBrush(pantsDark) : new SolidColorBrush(Color.FromRgb(45, 45, 43));
        var leftShoe = new Ellipse { Width = 35, Height = 15, Fill = shoeBrush };
        Canvas.SetLeft(leftShoe, 38);
        Canvas.SetTop(leftShoe, 204);
        canvas.Children.Add(leftShoe);
        var rightShoe = new Ellipse { Width = 35, Height = 15, Fill = shoeBrush };
        Canvas.SetLeft(rightShoe, 77);
        Canvas.SetTop(rightShoe, 204);
        canvas.Children.Add(rightShoe);

        if (!ghost)
        {
            var xbox = new TextBlock
            {
                Text = "XBOX",
                Foreground = new SolidColorBrush(accentColor),
                FontWeight = FontWeights.Bold,
                FontSize = 10
            };
            Canvas.SetLeft(xbox, 57);
            Canvas.SetTop(xbox, 97);
            canvas.Children.Add(xbox);
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

    private static FrameworkElement CreateMicrophoneGlyph()
    {
        var canvas = new Canvas { Width = 20, Height = 20, Margin = new Thickness(8, 0, 0, 0), IsHitTestVisible = false };
        var mic = new Border
        {
            Width = 7,
            Height = 12,
            BorderBrush = new SolidColorBrush(Color.FromRgb(103, 103, 103)),
            BorderThickness = new Thickness(1.4),
            CornerRadius = new CornerRadius(4)
        };
        Canvas.SetLeft(mic, 6);
        Canvas.SetTop(mic, 1);
        canvas.Children.Add(mic);
        var stem = new Rectangle { Width = 1.4, Height = 5, Fill = new SolidColorBrush(Color.FromRgb(103, 103, 103)) };
        Canvas.SetLeft(stem, 8.8);
        Canvas.SetTop(stem, 12);
        canvas.Children.Add(stem);
        return canvas;
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

    private void ShowCreateProfileForm()
    {
        _profileSigninCreating = true;
        if (_profileSigninCanvas != null)
        {
            _profileSigninCanvas.Visibility = Visibility.Collapsed;
        }
        if (_profileSigninCreatePanel != null)
        {
            _profileSigninCreatePanel.Visibility = Visibility.Visible;
        }
        if (_profileSigninTitle != null)
        {
            _profileSigninTitle.Text = "create profile";
        }
        if (_profileSigninSubtitle != null)
        {
            _profileSigninSubtitle.Text = "Choose your profile details";
        }
        if (_profileSigninCreateGamertag != null)
        {
            _profileSigninCreateGamertag.Text = string.Empty;
        }
        if (_profileSigninCreateName != null)
        {
            _profileSigninCreateName.Text = string.Empty;
        }
        if (_profileSigninCreateError != null)
        {
            _profileSigninCreateError.Text = string.Empty;
        }
        Dispatcher.BeginInvoke((Action)(() => _profileSigninCreateGamertag?.Focus()), DispatcherPriority.Input);
    }

    private void ShowProfileChooser()
    {
        _profileSigninCreating = false;
        if (_profileSigninCanvas != null)
        {
            _profileSigninCanvas.Visibility = Visibility.Visible;
        }
        if (_profileSigninCreatePanel != null)
        {
            _profileSigninCreatePanel.Visibility = Visibility.Collapsed;
        }
        if (_profileSigninTitle != null)
        {
            _profileSigninTitle.Text = "sign in or out";
        }
        if (_profileSigninSubtitle != null)
        {
            _profileSigninSubtitle.Text = "Choose your profile";
        }
    }

    private async Task CreateLocalProfileAsync()
    {
        string gamertag = _profileSigninCreateGamertag?.Text.Trim() ?? string.Empty;
        string name = _profileSigninCreateName?.Text.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(gamertag))
        {
            SetCreateProfileError("Enter a gamertag.");
            _profileSigninCreateGamertag?.Focus();
            return;
        }
        if (_profileSigninProfiles.Any(profile => string.Equals(profile.Gamertag?.Trim(), gamertag, StringComparison.OrdinalIgnoreCase)))
        {
            SetCreateProfileError("That gamertag already exists on this dashboard.");
            _profileSigninCreateGamertag?.Focus();
            return;
        }
        if (_profileCatalogService == null)
        {
            SetCreateProfileError("Profile storage is unavailable.");
            return;
        }

        var profile = new Profile
        {
            Gamertag = gamertag,
            Name = string.IsNullOrWhiteSpace(name) ? "(No name)" : name,
            GamerPicturePath = System.IO.Path.Combine("Assets", "Profile", "profilepicture.jpg"),
            Gamerscore = 0,
            OnlineStatus = "Online",
            Motto = "(No motto)",
            Location = string.IsNullOrWhiteSpace(_viewModel.Profile.Location) ? "United States" : _viewModel.Profile.Location,
            Description = "(No bio)"
        };

        try
        {
            _profileSigninCreateConfirmButton?.SetCurrentValue(IsEnabledProperty, false);
            _profileSigninProfiles.Add(profile);
            await _profileCatalogService.ActivateAsync(profile, _profileSigninProfiles);
            _viewModel.Profile = LocalProfileCatalogService.Clone(profile);
            RebuildProfileCards();
            ShowProfileChooser();
            if (_viewModel.CloseProfileEditorCommand.CanExecute(null))
            {
                _viewModel.CloseProfileEditorCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            _profileSigninProfiles.RemoveAll(item => string.Equals(item.ProfileId, profile.ProfileId, StringComparison.OrdinalIgnoreCase));
            App.LogException(ex, "ProfileSignin.CreateProfile");
            SetCreateProfileError("DashX360 could not save that profile.");
        }
        finally
        {
            _profileSigninCreateConfirmButton?.SetCurrentValue(IsEnabledProperty, true);
        }
    }

    private void SetCreateProfileError(string message)
    {
        if (_profileSigninCreateError != null)
        {
            _profileSigninCreateError.Text = message;
        }
    }

    private async Task ActivateProfileAsync(Profile profile)
    {
        if (_profileCatalogService == null)
        {
            return;
        }

        try
        {
            await _profileCatalogService.ActivateAsync(profile, _profileSigninProfiles);
            _viewModel.Profile = LocalProfileCatalogService.Clone(profile);
            if (_viewModel.CloseProfileEditorCommand.CanExecute(null))
            {
                _viewModel.CloseProfileEditorCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            App.LogException(ex, "ProfileSignin.ActivateProfile");
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
        if (!_viewModel.IsProfileEditorOpen)
        {
            return;
        }

        if (_profileSigninCreating)
        {
            HandleCreateProfileControllerAction(action);
            return;
        }

        var buttons = new List<Button>(_profileSigninProfileButtons);
        if (_profileSigninCreateButton != null)
        {
            buttons.Add(_profileSigninCreateButton);
        }
        if (buttons.Count == 0)
        {
            return;
        }

        Button? focused = Keyboard.FocusedElement as Button;
        int index = focused == null ? -1 : buttons.IndexOf(focused);
        if (index < 0)
        {
            index = Math.Max(0, buttons.IndexOf(_profileSigninPrimaryButton!));
        }

        if (action is DashboardInputAction.MoveLeft or DashboardInputAction.MoveUp)
        {
            buttons[Math.Max(0, index - 1)].Focus();
            return;
        }
        if (action is DashboardInputAction.MoveRight or DashboardInputAction.MoveDown)
        {
            buttons[Math.Min(buttons.Count - 1, index + 1)].Focus();
            return;
        }
        if (action == DashboardInputAction.Activate)
        {
            (focused ?? _profileSigninPrimaryButton ?? buttons[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return;
        }
        if (action == DashboardInputAction.Back && _viewModel.CloseProfileEditorCommand.CanExecute(null))
        {
            _viewModel.CloseProfileEditorCommand.Execute(null);
        }
    }

    private void HandleCreateProfileControllerAction(DashboardInputAction action)
    {
        if (action == DashboardInputAction.Back)
        {
            ShowProfileChooser();
            Dispatcher.BeginInvoke((Action)(() => _profileSigninCreateButton?.Focus()), DispatcherPriority.Input);
            return;
        }
        if (action == DashboardInputAction.MoveUp)
        {
            if (Keyboard.FocusedElement == _profileSigninCreateConfirmButton)
            {
                _profileSigninCreateName?.Focus();
            }
            else
            {
                _profileSigninCreateGamertag?.Focus();
            }
            return;
        }
        if (action == DashboardInputAction.MoveDown)
        {
            if (Keyboard.FocusedElement == _profileSigninCreateGamertag)
            {
                _profileSigninCreateName?.Focus();
            }
            else
            {
                _profileSigninCreateConfirmButton?.Focus();
            }
            return;
        }
        if (action == DashboardInputAction.Activate && Keyboard.FocusedElement == _profileSigninCreateConfirmButton)
        {
            _profileSigninCreateConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
    }

    private void ProfileSigninOnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_viewModel.IsProfileEditorOpen)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_profileSigninCreating)
            {
                ShowProfileChooser();
                _profileSigninCreateButton?.Focus();
            }
            else if (_viewModel.CloseProfileEditorCommand.CanExecute(null))
            {
                _viewModel.CloseProfileEditorCommand.Execute(null);
            }
            e.Handled = true;
        }
    }

    private async void ProfileSigninOnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, "Profile", StringComparison.Ordinal))
        {
            if (_viewModel.IsProfileEditorOpen)
            {
                await LoadProfileCatalogAndShowChooserAsync();
            }
            return;
        }
        if (!string.Equals(e.PropertyName, "IsProfileEditorOpen", StringComparison.Ordinal))
        {
            return;
        }

        if (_viewModel.IsProfileEditorOpen)
        {
            CaptureProfileSigninController();
            await LoadProfileCatalogAndShowChooserAsync();
        }
        else
        {
            RestoreProfileSigninControllerCapture();
            ShowProfileChooser();
        }
    }
}
