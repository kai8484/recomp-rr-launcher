using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Serilog;
using WheelWizard.Helpers;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Popups.Generic;
using SettingsButton = WheelWizard.Views.Components.Button;

namespace WheelWizard.Views.Pages.Settings;

public partial class WhWzSettings : UserControlBase
{
    private sealed record LanguageDropdownItem(string Key, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private readonly bool _pageLoaded;
    private bool _editingScale;
    private bool _isMovingAppData;
    private bool _updatingLanguageDropdown;

    [Inject]
    private ISettingsManager SettingsService { get; set; } = null!;

    [Inject]
    private ISettingsLocalizationService LocalizationService { get; set; } = null!;

    public WhWzSettings()
    {
        InitializeComponent();
        ConfigureLocationFieldsForActiveFrontend();
        UpdateLocationRows();
        LoadSettings();
        UpdateAppDataLocationUi();
        _pageLoaded = true;

        WhWzLanguageDropdown.SelectionChanged += WhWzLanguageDropdown_OnSelectionChanged;
    }

    private void ConfigureLocationFieldsForActiveFrontend()
    {
        GameLocationBorder.CornerRadius = new Avalonia.CornerRadius(12);
        ToolTip.SetTip(LocationWarningIcon, t("helper_text.must_set_game_path"));
    }

    private void LoadSettings()
    {
        // -----------------
        // Wheel Wizard Language Dropdown
        // -----------------
        RefreshLanguageDropdown();
        RefreshLocalizedCodeText();

        // -----------------
        // Window Scale settings
        // -----------------
        // IMPORTANT: Make sure that the number and percentage is always the last word in the string,
        // If you don't want this, you should change the code below that parses the string back to an actual value

        foreach (var scale in SettingValues.WindowScales)
        {
            WindowScaleDropdown.Items.Add(ScaleToString(scale));
        }

        var selectedItemText = ScaleToString((double)SettingsService.WINDOW_SCALE.Get());
        if (!WindowScaleDropdown.Items.Contains(selectedItemText))
            WindowScaleDropdown.Items.Add(selectedItemText);
        WindowScaleDropdown.SelectedItem = selectedItemText;

        EnableAnimations.IsChecked = (bool)SettingsService.ENABLE_ANIMATIONS.Get();
    }

    private void RefreshLanguageDropdown()
    {
        var currentWhWzLanguage = (string)SettingsService.WW_LANGUAGE.Get();
        _updatingLanguageDropdown = true;
        try
        {
            WhWzLanguageDropdown.Items.Clear();
            foreach (var (key, displayNameFactory) in SettingValues.WhWzLanguages)
            {
                WhWzLanguageDropdown.Items.Add(new LanguageDropdownItem(key, displayNameFactory()));
            }

            WhWzLanguageDropdown.SelectedItem = WhWzLanguageDropdown
                .Items.OfType<LanguageDropdownItem>()
                .FirstOrDefault(item => item.Key == currentWhWzLanguage);
        }
        finally
        {
            _updatingLanguageDropdown = false;
        }
    }

    private void RefreshLocalizedCodeText()
    {
        MarioKartHelperText.Text = t("helper_text.end_with_x") + " .iso/.gcm/.gcz/.ciso/.wbfs/.wia/.rvz";
        TranslationsPercentageText.Text = t("text.language_translated_by", t("value.language.z_translators"));
        TranslationsPercentageText.IsVisible = t("value.language.z_translators") != "-";
    }

    private static string ScaleToString(double scale)
    {
        var percentageString = (int)Math.Round(scale * 100) + "%";
        if (SettingValues.WindowScales.Contains(scale))
            return percentageString;

        return t("state.custom") + ": " + percentageString;
    }

    private async void GameLocationBrowse_OnClick(object sender, RoutedEventArgs e)
    {
        var fileType = new FilePickerFileType("Game files")
        {
            Patterns = ["*.iso", "*.gcm", "*.gcz", "*.ciso", "*.wbfs", "*.wia", "*.rvz"],
        };

        var filePath = await FilePickerHelper.OpenSingleFileAsync("Select Mario Kart Wii Game File", [fileType]);
        if (!string.IsNullOrEmpty(filePath))
        {
            await ApplyLocationSettingAsync(SettingsService.GAME_LOCATION, filePath);
        }
    }

    private async Task<bool> ApplyLocationSettingAsync(Setting setting, string path)
    {
        var normalizedPath = path;
        var previousPath = (string)setting.Get();

        if (!setting.Set(normalizedPath))
        {
            await MessageTranslationHelper.AwaitMessageAsync(MessageTranslation.Warning_InvalidPathSettings);
            UpdateLocationRows();
            return false;
        }

        UpdateLocationRows();
        await MessageTranslationHelper.AwaitMessageAsync(MessageTranslation.Success_PathSettingsSaved);
        return true;
    }

    private void GameLocationOpen_OnClick(object sender, RoutedEventArgs e) => OpenContainingFolder(PathManager.GameFilePath);

    private static void OpenContainingFolder(string filePath)
    {
        try
        {
            var unquotedPath = filePath.Trim().Trim('\'', '"');
            var folderPath = Path.GetDirectoryName(Path.GetFullPath(unquotedPath));
            if (!string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath))
                FilePickerHelper.OpenFolderInFileManager(folderPath);
        }
        catch { }
    }

    private void AppDataLocationOpen_OnClick(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(PathManager.WheelWizardAppdataPath))
            Directory.CreateDirectory(PathManager.WheelWizardAppdataPath);

        FilePickerHelper.OpenFolderInFileManager(PathManager.WheelWizardAppdataPath);
    }

    private void UpdateLocationRows()
    {
        SetLocationRowState(
            GameLocationCompleteIcon,
            GameLocationWarningIcon,
            GameLocationChangeButton,
            SettingsService.GAME_LOCATION.IsValid() && !string.IsNullOrWhiteSpace(PathManager.GameFilePath)
        );

        LocationWarningIcon.IsVisible = !SettingsService.PathsSetupCorrectly();
        GameLocationOpenButton.IsEnabled = CanOpenContainingFolder(PathManager.GameFilePath);
    }

    private static void SetLocationRowState(PathIcon completeIcon, PathIcon warningIcon, SettingsButton changeButton, bool isValid)
    {
        completeIcon.IsVisible = isValid;
        warningIcon.IsVisible = !isValid;
        changeButton.Variant = isValid ? SettingsButton.ButtonsVariantType.Default : SettingsButton.ButtonsVariantType.Warning;
    }

    private static bool CanOpenContainingFolder(string filePath)
    {
        try
        {
            var unquotedPath = filePath.Trim().Trim('\'', '"');
            var folderPath = Path.GetDirectoryName(Path.GetFullPath(unquotedPath));
            return !string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsConfiguredExecutableFile(string value) => File.Exists(value.Trim().Trim('\'', '"'));

    private void UpdateAppDataLocationUi()
    {
        var statusText =
            _isMovingAppData ? t("status.data_folder.moving")
            : PathManager.IsUsingCustomWheelWizardAppdataPath ? t("status.data_folder.custom")
            : string.Empty;

        AppDataLocationStatus.Text = statusText;
        AppDataLocationStatus.IsVisible = !string.IsNullOrEmpty(statusText);
        AppDataLocationChangeButton.IsEnabled = !_isMovingAppData;
        AppDataLocationResetButton.IsEnabled = !_isMovingAppData && PathManager.IsUsingCustomWheelWizardAppdataPath;
        AppDataLocationOpenButton.IsEnabled = !_isMovingAppData;
        ToolTip.SetTip(AppDataLocationResetButton, PathManager.DefaultWheelWizardAppdataFolderPath);
    }

    private void SetAppDataLocationBusyState(bool isBusy)
    {
        _isMovingAppData = isBusy;
        AppDataLocationChangeButton.IsEnabled = !isBusy;
        AppDataLocationResetButton.IsEnabled = !isBusy && PathManager.IsUsingCustomWheelWizardAppdataPath;
        AppDataLocationOpenButton.IsEnabled = !isBusy;
        if (isBusy)
        {
            AppDataLocationStatus.Text = t("status.data_folder.moving");
            AppDataLocationStatus.IsVisible = true;
        }
    }

    private async Task<bool> ConfirmAndMoveAppDataAsync(string targetPath)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            return false;

        var trimmedTarget = targetPath.Trim();

        var validationSuccessful = PathManager.TryValidateWheelWizardAppdataTarget(
            trimmedTarget,
            out var normalizedTarget,
            out _,
            out var validationError,
            out var requiresMove
        );

        if (!validationSuccessful)
        {
            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText(t("message_error.data_folder_move.title"))
                .SetInfoText(validationError)
                .ShowDialog();
            return false;
        }

        if (!requiresMove)
            return false;

        var extraText =
            t("question.move_data.extra", normalizedTarget)
            ?? $"Wheel Wizard will move its files to:\n{normalizedTarget}\nThis may take a while depending on the amount of data.";

        var confirmed = await new YesNoWindow()
            .SetMainText(t("question.move_data.title"))
            .SetExtraText(extraText)
            .SetButtonText(t("action.yes"), t("action.no"))
            .AwaitAnswer();

        if (!confirmed)
            return false;

        await MoveWheelWizardDataAsync(normalizedTarget);
        return true;
    }

    private async Task MoveWheelWizardDataAsync(string targetPath)
    {
        SetAppDataLocationBusyState(true);
        Log.CloseAndFlush();

        var progressWindow = new ProgressWindow(t("status.data_folder.moving"))
            .SetExtraText(t("helper_text.wheel_wizard_data_folder"))
            .SetGoal(t("status.data_folder.moving"));
        progressWindow.Show();

        var progress = new Progress<double>(value =>
        {
            var percentage = (int)Math.Clamp(Math.Round(value * 100), 0, 100);
            progressWindow.UpdateProgress(percentage);
        });

        (bool success, string errorMessage, DirectoryMoveContentsResult details) moveResult;
        try
        {
            moveResult = await Task.Run(() =>
            {
                var moveSuccessful = PathManager.TrySetWheelWizardAppdataPath(targetPath, out var error, out var moveDetails, progress);
                return (moveSuccessful, error, moveDetails);
            });
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            WheelWizard.Logging.RecreateStaticLogger();
            SetAppDataLocationBusyState(false);
            UpdateAppDataLocationUi();

            await new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText(t("message_error.data_folder_move.title"))
                .SetInfoText(ex.Message)
                .ShowDialog();
            return;
        }

        progressWindow.Close();

        WheelWizard.Logging.RecreateStaticLogger();

        SetAppDataLocationBusyState(false);
        UpdateAppDataLocationUi();

        var (success, errorMessage, details) = moveResult;

        if (success)
        {
            await HandleSuccessfulAppdataMoveAsync(details, errorMessage);
        }
        else
        {
            await HandleFailedAppdataMoveAsync(details, errorMessage);
        }
    }

    private async Task HandleSuccessfulAppdataMoveAsync(DirectoryMoveContentsResult moveDetails, string warningMessage)
    {
        if (moveDetails.Outcome == DirectoryMoveOutcome.SourceDeletionFailed)
        {
            var prompt = new YesNoWindow()
                .SetMainText("Unable to delete old data folder")
                .SetExtraText(
                    "The previous Wheel Wizard data folder could not be removed."
                        + $"\n\nOld location:\n{moveDetails.SourcePath}\n\n"
                        + $"New location:\n{moveDetails.DestinationPath}\n\n"
                        + "Select Revert to undo the move or Continue to keep using the new folder and leave the old files."
                )
                .SetButtonText("Revert", "Continue");

            var revert = await prompt.AwaitAnswer();
            if (revert)
            {
                var revertSucceeded = PathManager.TryRevertWheelWizardAppdataMove(
                    moveDetails.SourcePath,
                    moveDetails.DestinationPath,
                    out var revertError
                );
                WheelWizard.Logging.RecreateStaticLogger();
                UpdateAppDataLocationUi();

                if (!revertSucceeded)
                {
                    await new MessageBoxWindow()
                        .SetMessageType(MessageBoxWindow.MessageType.Error)
                        .SetTitleText(t("message_error.data_folder_move.title"))
                        .SetInfoText(revertError)
                        .ShowDialog();
                }
                else
                {
                    await new MessageBoxWindow()
                        .SetMessageType(MessageBoxWindow.MessageType.Message)
                        .SetTitleText("Data folder move reverted")
                        .SetInfoText($"Wheel Wizard will continue using:\n{moveDetails.SourcePath}")
                        .ShowDialog();
                }

                return;
            }
        }

        var infoText =
            t("message_success.data_folder_moved.extra", PathManager.WheelWizardAppdataPath)
            ?? $"Wheel Wizard data is now stored in:\n{PathManager.WheelWizardAppdataPath}";

        if (!string.IsNullOrWhiteSpace(warningMessage))
            infoText += $"\n\n{warningMessage}";

        await new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Message)
            .SetTitleText(t("message_success.data_folder_moved.title"))
            .SetInfoText(infoText)
            .ShowDialog();
    }

    private async Task HandleFailedAppdataMoveAsync(DirectoryMoveContentsResult moveDetails, string errorMessage)
    {
        var infoText = string.IsNullOrWhiteSpace(errorMessage) ? "Failed to move the Wheel Wizard data folder." : errorMessage;

        await new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Error)
            .SetTitleText(t("message_error.data_folder_move.title"))
            .SetInfoText(infoText)
            .ShowDialog();

        if (moveDetails.Outcome is DirectoryMoveOutcome.CopyFailed or DirectoryMoveOutcome.VerificationFailed)
        {
            var prompt = new YesNoWindow()
                .SetMainText("Revert changes?")
                .SetExtraText(
                    $"Wheel Wizard left a partial copy in:\n{moveDetails.DestinationPath}\n\n"
                        + "Choose Revert to delete it now, or Continue to leave the files in place."
                )
                .SetButtonText("Revert", "Continue");

            var revert = await prompt.AwaitAnswer();
            if (revert)
            {
                var cleaned = PathManager.TryCleanupPartialWheelWizardAppdataMove(moveDetails.DestinationPath, out var cleanupError);
                if (!cleaned)
                {
                    await new MessageBoxWindow()
                        .SetMessageType(MessageBoxWindow.MessageType.Error)
                        .SetTitleText("Unable to remove partial files")
                        .SetInfoText(cleanupError)
                        .ShowDialog();
                }
                else
                {
                    await new MessageBoxWindow()
                        .SetMessageType(MessageBoxWindow.MessageType.Message)
                        .SetTitleText("Partial files removed")
                        .SetInfoText($"Removed folder:\n{moveDetails.DestinationPath}")
                        .ShowDialog();
                }
            }
        }
    }

    private async void AppDataLocationChange_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isMovingAppData)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        IStorageFolder? suggestedStart = null;

        var currentPath = PathManager.WheelWizardAppdataPath;
        if (!string.IsNullOrWhiteSpace(currentPath) && Directory.Exists(currentPath))
            suggestedStart = await topLevel!.StorageProvider.TryGetFolderFromPathAsync(currentPath);

        var folders = await FilePickerHelper.SelectFolderAsync("Select Wheel Wizard data folder", suggestedStart);
        if (folders == null || folders.Count == 0)
            return;

        var resolvedPath = await ResolveSelectedFolderPathAsync(folders[0]);
        if (!string.IsNullOrWhiteSpace(resolvedPath))
            await ConfirmAndMoveAppDataAsync(resolvedPath);
    }

    private async void AppDataLocationReset_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isMovingAppData || !PathManager.IsUsingCustomWheelWizardAppdataPath)
            return;

        await ConfirmAndMoveAppDataAsync(PathManager.DefaultWheelWizardAppdataFolderPath);
    }

    private async void WindowScaleDropdown_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_pageLoaded || _editingScale)
            return;

        _editingScale = true;
        var selectedScale = WindowScaleDropdown.SelectedItem?.ToString() ?? "1";
        var scale = double.Parse(selectedScale.Split(" ").Last().Replace("%", "")) / 100;
        scale = ViewUtils.GetUsableWindowScale(scale, new Avalonia.Size(Layout.WindowWidth, Layout.WindowHeight), ViewUtils.GetLayout());
        var selectedItemText = ScaleToString(scale);
        if (!WindowScaleDropdown.Items.Contains(selectedItemText))
            WindowScaleDropdown.Items.Add(selectedItemText);
        WindowScaleDropdown.SelectedItem = selectedItemText;

        if (!SettingsService.WINDOW_SCALE.Set(scale))
        {
            WindowScaleDropdown.SelectedItem = ScaleToString((double)SettingsService.WINDOW_SCALE.Get());
            _editingScale = false;
            return;
        }
        var seconds = 10;

        string ExtraScaleText() => t("question.apply_scale.extra", tTime(seconds));

        var yesNoWindow = new YesNoWindow()
            .SetButtonText(t("action.apply"), t("action.revert"))
            .SetMainText(t("question.apply_scale.title"))
            .SetExtraText(ExtraScaleText());
        // we want to now set up a timer every second to update the text, and at the last second close the window
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };

        timer.Tick += (_, args) =>
        {
            seconds--;
            yesNoWindow.SetExtraText(ExtraScaleText());
            if (seconds != 0)
                return;
            yesNoWindow.Close();
            timer.Stop();
        };
        timer.Start();

        var yesNoAnswer = await yesNoWindow.AwaitAnswer();
        if (yesNoAnswer)
            SettingsService.SAVED_WINDOW_SCALE.Set(SettingsService.WINDOW_SCALE.Get());
        else
        {
            SettingsService.WINDOW_SCALE.Set(SettingsService.SAVED_WINDOW_SCALE.Get());
            WindowScaleDropdown.SelectedItem = ScaleToString((double)SettingsService.WINDOW_SCALE.Get());
        }

        _editingScale = false;
    }

    private async Task<string?> ResolveSelectedFolderPathAsync(IStorageFolder? folder)
    {
        if (folder == null)
            return null;

        var resolved = FilePickerHelper.TryResolveLocalPath(folder);
        if (!string.IsNullOrWhiteSpace(resolved))
            return resolved;

        await ShowFolderSelectionErrorAsync();
        return null;
    }

    private Task ShowFolderSelectionErrorAsync()
    {
        return new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Error)
            .SetTitleText(t("message_error.data_folder_move.title"))
            .SetInfoText("Wheel Wizard couldn't resolve the selected folder. Please choose a different location.")
            .ShowDialog();
    }

    private async void WhWzLanguageDropdown_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingLanguageDropdown)
            return;

        if (WhWzLanguageDropdown.SelectedItem == null)
            return;

        if (WhWzLanguageDropdown.SelectedItem is not LanguageDropdownItem selectedLanguage)
            return;

        var currentLanguage = (string)SettingsService.WW_LANGUAGE.Get();
        if (selectedLanguage.Key == currentLanguage)
            return;

        var titleCurrent = t($"{currentLanguage}.question.apply_language_settings.title");
        var titleTarget = t($"{selectedLanguage.Key}.question.apply_language_settings.title");

        var extraCurrent = t($"{currentLanguage}.question.apply_language_settings.extra");
        var extraTarget = t($"{selectedLanguage.Key}.question.apply_language_settings.extra");

        // popup now shows its selection in both languages
        var yesNoWindow = await new YesNoWindow()
            .SetMainText($"{titleCurrent}\n\n{titleTarget}")
            .SetExtraText($"{extraCurrent}\n\n{extraTarget}")
            .SetButtonText(t("action.apply"), t("action.cancel"))
            .AwaitAnswer();

        if (!yesNoWindow)
        {
            var currentWhWzLanguage = (string)SettingsService.WW_LANGUAGE.Get();
            WhWzLanguageDropdown.SelectedItem = WhWzLanguageDropdown
                .Items.OfType<LanguageDropdownItem>()
                .FirstOrDefault(item => item.Key == currentWhWzLanguage);
            return; // We only want to change the setting if we really apply this change
        }

        if (SettingsService.WW_LANGUAGE.Set(selectedLanguage.Key))
        {
            LocalizationService.ApplyCurrentLanguage();
            RefreshLanguageDropdown();
            RefreshLocalizedCodeText();
            ViewUtils.RefreshWindow();
        }
    }

    private void EnableAnimations_OnClick(object sender, RoutedEventArgs e) =>
        SettingsService.ENABLE_ANIMATIONS.Set(EnableAnimations.IsChecked == true);
}
