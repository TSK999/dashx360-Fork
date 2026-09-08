using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using XboxMetroLauncher.Input;
using XboxMetroLauncher.Models;
using XboxMetroLauncher.Services;
using XboxMetroLauncher.Utilities;

namespace XboxMetroLauncher.Views;

public partial class FirstRunSetupWindow : Window
{
    private readonly FirstRunSetupService _setupService;
    private readonly IGameLibraryService _libraryService;
    private readonly ISteamLibraryScannerService _steamScanner;
    private readonly IProfileService _profileService;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ControllerInputService _controllerInput;
    private FirstRunSetupState _state = new();
    private int _step;
    private bool _steamScanCompleted;
    private bool _busy;

    public FirstRunSetupWindow(
        FirstRunSetupService setupService,
        IGameLibraryService libraryService,
        ISteamLibraryScannerService steamScanner,
        IProfileService profileService)
    {
        _setupService = setupService;
        _libraryService = libraryService;
        _steamScanner = steamScanner;
        _profileService = profileService;
        InitializeComponent();

        _controllerInput = new ControllerInputService(HandleControllerAction, () => IsVisible && !_busy);
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _lifetime.Cancel();
            _controllerInput.Dispose();
        };

        LanguageList.MouseDoubleClick += async (_, _) => await NextAsync();
        LocaleList.MouseDoubleClick += async (_, _) => await NextAsync();
        SystemPanel.MouseLeftButtonUp += async (_, _) => await NextAsync();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _state = await _setupService.LoadAsync(_lifetime.Token);
            ApplyStateToControls();
            ShowStep(0);
            BeginButton.Focus();
            _controllerInput.Start();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            App.LogException(ex, "FirstRunSetup.Load");
            _state = new FirstRunSetupState();
            ShowStep(0);
            _controllerInput.Start();
        }
    }

    private void ApplyStateToControls()
    {
        SelectListItem(LanguageList, _state.Language);
        SelectListItem(LocaleList, _state.Locale);
        GamertagBox.Text = string.IsNullOrWhiteSpace(_state.Gamertag) ? "Player" : _state.Gamertag;
    }

    private static void SelectListItem(ListBox list, string value)
    {
        foreach (object item in list.Items)
        {
            if (item is ListBoxItem listItem && string.Equals(listItem.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                list.SelectedItem = listItem;
                return;
            }
        }
    }

    private static string SelectedText(ListBox list, string fallback)
    {
        return (list.SelectedItem as ListBoxItem)?.Content?.ToString() ?? fallback;
    }

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, 6);
        WelcomePanel.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        LanguagePanel.Visibility = _step == 1 ? Visibility.Visible : Visibility.Collapsed;
        LocalePanel.Visibility = _step == 2 ? Visibility.Visible : Visibility.Collapsed;
        ProfilePanel.Visibility = _step == 3 ? Visibility.Visible : Visibility.Collapsed;
        SystemPanel.Visibility = _step == 4 ? Visibility.Visible : Visibility.Collapsed;
        SteamPanel.Visibility = _step == 5 ? Visibility.Visible : Visibility.Collapsed;
        ReadyPanel.Visibility = _step == 6 ? Visibility.Visible : Visibility.Collapsed;

        BackBadge.Visibility = _step == 0 || _busy ? Visibility.Collapsed : Visibility.Visible;
        BackHint.Visibility = _step == 0 || _busy ? Visibility.Collapsed : Visibility.Visible;
        AcceptHint.Text = _step switch
        {
            0 => "Begin",
            5 => "Choose",
            6 => "Finish",
            _ => "Next"
        };

        UIElement visiblePanel = WelcomePanel;
        switch (_step)
        {
            case 0:
                StepLabel.Text = "Initial Setup";
                SectionTitle.Text = "Welcome";
                SectionDescription.Text = "Before you enter the dashboard, choose the same essentials an Xbox 360 asks for on first startup — then we'll find your PC games.";
                BeginButton.Focus();
                visiblePanel = WelcomePanel;
                break;
            case 1:
                StepLabel.Text = "1 of 6 · Language";
                SectionTitle.Text = "Language";
                SectionDescription.Text = "Choose the language you want to use on the dashboard.";
                LanguageList.Focus();
                visiblePanel = LanguagePanel;
                break;
            case 2:
                StepLabel.Text = "2 of 6 · Locale";
                SectionTitle.Text = "Locale";
                SectionDescription.Text = "Your location is used for profile defaults and regional presentation.";
                LocaleList.Focus();
                visiblePanel = LocalePanel;
                break;
            case 3:
                StepLabel.Text = "3 of 6 · Profile";
                SectionTitle.Text = "Your Profile";
                SectionDescription.Text = "Create the local profile that appears in the Guide and dashboard header.";
                GamertagBox.Focus();
                GamertagBox.SelectAll();
                visiblePanel = ProfilePanel;
                break;
            case 4:
                StepLabel.Text = "4 of 6 · System";
                SectionTitle.Text = "System";
                SectionDescription.Text = "DashX360 uses your Windows display, sound and network configuration rather than replacing those settings.";
                DisplayValue.Text = $"{Math.Round(SystemParameters.PrimaryScreenWidth)} × {Math.Round(SystemParameters.PrimaryScreenHeight)}";
                NetworkValue.Text = NetworkInterface.GetIsNetworkAvailable() ? "Connected" : "Not connected";
                SystemPanel.Focus();
                visiblePanel = SystemPanel;
                break;
            case 5:
                StepLabel.Text = "5 of 6 · Games";
                SectionTitle.Text = "Game Library";
                SectionDescription.Text = "Like the final console setup step, this prepares your content. On PC, DashX360 can automatically discover installed Steam games.";
                if (!_busy)
                {
                    ImportSteamButton.Focus();
                }
                visiblePanel = SteamPanel;
                break;
            case 6:
                StepLabel.Text = "6 of 6 · Complete";
                SectionTitle.Text = "Welcome";
                SectionDescription.Text = "Setup is complete. Your dashboard is ready.";
                ReadySummary.Text = BuildReadySummary();
                FinishButton.Focus();
                visiblePanel = ReadyPanel;
                break;
        }

        AnimatePanel(visiblePanel);
    }

    private static void AnimatePanel(UIElement panel)
    {
        panel.Opacity = 0;
        var transform = new TranslateTransform(26, 0);
        panel.RenderTransform = transform;
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
        transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(26, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        });
    }

    private string BuildReadySummary()
    {
        string games = _steamScanCompleted ? "Your Steam library has been checked and saved." : "You can scan Steam later from Settings.";
        return $"Profile: {_state.Gamertag}\nLocation: {_state.Locale}\n\n{games}";
    }

    private async Task SaveProgressAsync()
    {
        _state.Language = SelectedText(LanguageList, "English");
        _state.Locale = SelectedText(LocaleList, "United States");
        _state.Gamertag = NormalizeGamertag(GamertagBox.Text);
        await _setupService.SaveAsync(_state, _lifetime.Token);
    }

    private static string NormalizeGamertag(string value)
    {
        string text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Player";
        }

        return text.Length <= 15 ? text : text[..15];
    }

    private async Task NextAsync()
    {
        if (_busy)
        {
            return;
        }

        if (_step == 3)
        {
            GamertagBox.Text = NormalizeGamertag(GamertagBox.Text);
        }

        await SaveProgressAsync();
        if (_step < 6)
        {
            ShowStep(_step + 1);
        }
    }

    private async void BeginButton_OnClick(object sender, RoutedEventArgs e)
    {
        await NextAsync();
    }

    private async void ImportSteamButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _state.ImportSteamLibrary = true;
        ImportSteamButton.IsEnabled = false;
        SkipSteamButton.IsEnabled = false;
        SteamProgress.Visibility = Visibility.Visible;
        SteamStatus.Text = "Looking for installed Steam games...";
        BackBadge.Visibility = Visibility.Collapsed;
        BackHint.Visibility = Visibility.Collapsed;

        try
        {
            GameLibrary library = await _libraryService.LoadAsync(_lifetime.Token);
            var progress = new Progress<LibraryScanProgress>(p =>
            {
                SteamStatus.Text = string.IsNullOrWhiteSpace(p.Location)
                    ? $"Scanning... {p.Completed}"
                    : $"Scanning {p.Location}";
            });

            SteamGameScanResult result = await _steamScanner.ScanAsync(library, _lifetime.Token, progress);
            await _libraryService.SaveAsync(library, _lifetime.Token);
            _steamScanCompleted = true;
            SteamStatus.Text = result.Message;
            SteamProgress.Visibility = Visibility.Collapsed;
            ImportSteamButton.Content = "scan again";
            SkipSteamButton.Content = "continue";
        }
        catch (OperationCanceledException)
        {
            SteamStatus.Text = "Steam scan cancelled.";
        }
        catch (Exception ex)
        {
            App.LogException(ex, "FirstRunSetup.SteamImport");
            SteamStatus.Text = "Steam couldn't be scanned right now. You can try again later from Settings.";
        }
        finally
        {
            _busy = false;
            ImportSteamButton.IsEnabled = true;
            SkipSteamButton.IsEnabled = true;
            BackBadge.Visibility = Visibility.Visible;
            BackHint.Visibility = Visibility.Visible;
            SkipSteamButton.Focus();
        }
    }

    private async void SkipSteamButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        _state.ImportSteamLibrary = _steamScanCompleted;
        await SaveProgressAsync();
        ShowStep(6);
    }

    private async void FinishButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        try
        {
            _state.Language = SelectedText(LanguageList, "English");
            _state.Locale = SelectedText(LocaleList, "United States");
            _state.Gamertag = NormalizeGamertag(GamertagBox.Text);

            Profile profile = await _profileService.LoadAsync(_lifetime.Token);
            profile.Gamertag = _state.Gamertag;
            profile.Location = _state.Locale;
            await _profileService.SaveAsync(profile, _lifetime.Token);

            _state.Completed = true;
            await _setupService.SaveAsync(_state, _lifetime.Token);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            App.LogException(ex, "FirstRunSetup.Finish");
            MessageBox.Show(this, "Setup could not be saved. Please try again.", "DashX360", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Window_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (e.Key == Key.Escape && _step > 0)
        {
            ShowStep(_step - 1);
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
        {
            return;
        }

        if (_step is >= 1 and <= 4)
        {
            await NextAsync();
            e.Handled = true;
        }
        else if (_step == 6)
        {
            FinishButton_OnClick(FinishButton, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void HandleControllerAction(DashboardInputAction action)
    {
        if (_busy)
        {
            return;
        }

        switch (action)
        {
            case DashboardInputAction.MoveUp:
                MoveFocus(FocusNavigationDirection.Up);
                break;
            case DashboardInputAction.MoveDown:
                MoveFocus(FocusNavigationDirection.Down);
                break;
            case DashboardInputAction.MoveLeft:
                MoveFocus(FocusNavigationDirection.Left);
                break;
            case DashboardInputAction.MoveRight:
                MoveFocus(FocusNavigationDirection.Right);
                break;
            case DashboardInputAction.Back:
                if (_step > 0)
                {
                    ShowStep(_step - 1);
                }
                break;
            case DashboardInputAction.Activate:
                ActivateCurrentSelection();
                break;
        }
    }

    private static void MoveFocus(FocusNavigationDirection direction)
    {
        if (Keyboard.FocusedElement is UIElement focused)
        {
            focused.MoveFocus(new TraversalRequest(direction));
        }
    }

    private async void ActivateCurrentSelection()
    {
        if (Keyboard.FocusedElement is Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            return;
        }

        if (_step is >= 1 and <= 4)
        {
            await NextAsync();
            return;
        }

        if (_step == 6)
        {
            FinishButton_OnClick(FinishButton, new RoutedEventArgs());
        }
    }
}
