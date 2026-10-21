using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using dlss_updater.Services;

namespace dlss_updater
{
    public partial class MainWindow : Window
    {
        private static readonly SolidColorBrush NeutralStatusBrush =
            new SolidColorBrush(Color.FromRgb(102, 102, 102));
        private static readonly SolidColorBrush SuccessStatusBrush =
            new SolidColorBrush(Color.FromRgb(46, 125, 50));

        private List<ScannedFile> _dlssFiles =
            new List<ScannedFile>();

        private List<ScannedFile> _dlssdFiles =
            new List<ScannedFile>();

        private List<ScannedFile> _dlssgFiles =
            new List<ScannedFile>();

        private List<ScannedFile> _backupFiles =
            new List<ScannedFile>();

        private string _scannedFolderPath = string.Empty;
        private CancellationTokenSource _folderPathDebounce;
        private int _folderScanGeneration;
        private bool _suppressFolderPathChange;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void BrowseButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            WindowInteropHelper helper =
                new WindowInteropHelper(this);

            string folder =
                FolderPicker.PickFolder(
                    helper.Handle);

            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            if (!TryNormalizeFolder(folder, true, out folder))
            {
                return;
            }

            await ScanFolderAsync(folder);
        }

        private async void FolderPathTextBox_KeyDown(
            object sender,
            KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            e.Handled = true;
            CancelPendingFolderPathScan();

            string folder;
            if (!TryNormalizeFolder(FolderPathTextBox.Text, true, out folder))
            {
                return;
            }

            SetFolderPathText(folder);
            await ScanFolderAsync(folder);
        }

        private void FolderPathTextBox_TextChanged(
            object sender,
            TextChangedEventArgs e)
        {
            if (_suppressFolderPathChange)
            {
                return;
            }

            string enteredFolder = NormalizeFolderText(FolderPathTextBox.Text);
            if (!string.IsNullOrWhiteSpace(_scannedFolderPath) &&
                string.Equals(
                enteredFolder,
                _scannedFolderPath,
                StringComparison.OrdinalIgnoreCase))
            {
                _folderScanGeneration++;
                CancelPendingFolderPathScan();
                UpdateDlssSummary();
                UpdateBackupResult(_backupFiles);
                OverwriteToggle.IsEnabled = true;
                bool hasDlssFiles =
                    _dlssFiles.Count + _dlssdFiles.Count + _dlssgFiles.Count > 0;
                UpdateButton.IsEnabled = hasDlssFiles;
                UpdateButton.Opacity = hasDlssFiles ? 1.0 : 0.5;
                return;
            }

            _folderScanGeneration++;
            CancelPendingFolderPathScan();
            ResetFolderScanState();

            if (string.IsNullOrWhiteSpace(enteredFolder))
            {
                return;
            }

            SetStatus(DlssStatusText, "Waiting to validate folder...", NeutralStatusBrush);

            CancellationTokenSource debounce = new CancellationTokenSource();
            _folderPathDebounce = debounce;
            int generation = _folderScanGeneration;
            StartFolderPathDebounce(enteredFolder, generation, debounce.Token);
        }

        private async void StartFolderPathDebounce(
            string enteredFolder,
            int generation,
            CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(650, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            if (generation != _folderScanGeneration)
            {
                return;
            }

            string folder;
            if (!TryNormalizeFolder(enteredFolder, false, out folder))
            {
                ResetFolderScanState();
                return;
            }

            SetFolderPathText(folder);
            await ScanFolderAsync(folder);
        }

        private void ResetFolderScanState()
        {
            _scannedFolderPath = string.Empty;
            _dlssFiles = new List<ScannedFile>();
            _dlssdFiles = new List<ScannedFile>();
            _dlssgFiles = new List<ScannedFile>();
            _backupFiles = new List<ScannedFile>();

            DlssSectionCard.IsEnabled = false;
            DlssSectionCard.Opacity = 0.5;
            BackupSectionCard.IsEnabled = false;
            BackupSectionCard.Opacity = 0.5;
            SetStatus(DlssStatusText, string.Empty, NeutralStatusBrush);
            SetStatus(BackupStatusText, string.Empty, NeutralStatusBrush);

            UpdateButton.IsEnabled = false;
            UpdateButton.Opacity = 0.5;
            OverwriteToggle.IsEnabled = false;
            SetBackupButtonsEnabled(false);
            DetailsFileListPanel.Children.Clear();
            DetailsSummaryText.Text = string.Empty;
            ShowDetailsButton.Visibility = Visibility.Collapsed;
        }

        private void CancelPendingFolderPathScan()
        {
            if (_folderPathDebounce == null)
            {
                return;
            }

            _folderPathDebounce.Cancel();
            _folderPathDebounce.Dispose();
            _folderPathDebounce = null;
        }

        private void SetFolderPathText(string folder)
        {
            _suppressFolderPathChange = true;
            FolderPathTextBox.Text = folder;
            _suppressFolderPathChange = false;
        }

        private bool TryNormalizeFolder(
            string folder,
            bool showError,
            out string normalizedFolder)
        {
            normalizedFolder = NormalizeFolderText(folder);

            try
            {
                if (!string.IsNullOrWhiteSpace(normalizedFolder))
                {
                    normalizedFolder = System.IO.Path.GetFullPath(normalizedFolder);
                }
            }
            catch
            {
                normalizedFolder = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(normalizedFolder) ||
                IsRestrictedSearchFolder(normalizedFolder) ||
                !System.IO.Directory.Exists(normalizedFolder))
            {
                if (showError)
                {
                    MessageBox.Show(
                        this,
                        "Please enter or select a valid directory.",
                        "Directory Not Supported",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return false;
            }

            return true;
        }

        private string NormalizeFolderText(string folder)
        {
            if (folder == null)
            {
                return string.Empty;
            }

            string normalized = folder.Trim();
            if (normalized.Length >= 2 &&
                normalized[0] == '"' &&
                normalized[normalized.Length - 1] == '"')
            {
                normalized = normalized.Substring(1, normalized.Length - 2).Trim();
            }

            return normalized;
        }

        private async Task ScanFolderAsync(string folder)
        {
            CancelPendingFolderPathScan();
            SetFolderPathText(folder);
            int generation = ++_folderScanGeneration;

            DlssSectionCard.IsEnabled = true;
            DlssSectionCard.Opacity = 1.0;
            BackupSectionCard.IsEnabled = true;
            BackupSectionCard.Opacity = 1.0;
            OverwriteToggle.IsEnabled = true;
            UpdateButton.IsEnabled = false;
            UpdateButton.Opacity = 0.5;

            ResetFileLists();
            SetSearchingState();
            SetStatus(BackupStatusText, "Searching for backup...", NeutralStatusBrush);
            RestoreBackupButton.IsEnabled = false;
            DeleteBackupButton.IsEnabled = false;

            Stopwatch stopwatch =
                Stopwatch.StartNew();

            Task<List<ScannedFile>> dlssTask =
                Task.Run(() =>
                    FolderScanner.FindFiles(
                        folder,
                        "nvngx_dlss.dll"));

            Task<List<ScannedFile>> dlssdTask =
                Task.Run(() =>
                    FolderScanner.FindFiles(
                        folder,
                        "nvngx_dlssd.dll"));

            Task<List<ScannedFile>> dlssgTask =
                Task.Run(() =>
                    FolderScanner.FindFiles(
                        folder,
                        "nvngx_dlssg.dll"));

            Task<List<ScannedFile>> backupTask =
                Task.Run(() =>
                    BackupScanner.FindBackupFiles(folder));

            await Task.WhenAll(
                dlssTask,
                dlssdTask,
                dlssgTask,
                backupTask);

            if (generation != _folderScanGeneration)
            {
                return;
            }

            stopwatch.Stop();

            if (stopwatch.ElapsedMilliseconds < 500)
            {
                await Task.Delay(
                    500 -
                    (int)stopwatch.ElapsedMilliseconds);
            }

            if (generation != _folderScanGeneration)
            {
                return;
            }

            _dlssFiles = await dlssTask;
            _dlssdFiles = await dlssdTask;
            _dlssgFiles = await dlssgTask;
            _backupFiles = await backupTask;
            UpdateDlssSummary();
            PopulateDlssDetails(folder);
            UpdateBackupResult(_backupFiles);

            bool hasDlssFiles =
                _dlssFiles.Count + _dlssdFiles.Count + _dlssgFiles.Count > 0;
            UpdateButton.IsEnabled = hasDlssFiles;
            UpdateButton.Opacity = hasDlssFiles ? 1.0 : 0.5;
            _scannedFolderPath = folder;
        }

        private async void UpdateButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string folder = FolderPathTextBox.Text;
            List<ScannedFile> filesToUpdate =
                new List<ScannedFile>();

            filesToUpdate.AddRange(_dlssFiles);
            filesToUpdate.AddRange(_dlssdFiles);
            filesToUpdate.AddRange(_dlssgFiles);

            UpdateButton.IsEnabled = false;
            UpdateButton.Content = "Updating...";

            try
            {
                DlssUpdateResult result =
                    await GameFolderUpdateService.UpdateAsync(
                        filesToUpdate,
                        OverwriteToggle.IsChecked == true);

                _backupFiles = await Task.Run(() =>
                    BackupScanner.FindBackupFiles(folder));

                InvokeOnUiThread(() =>
                {
                    UpdateDlssSummary();
                    PopulateDlssDetails(folder);
                    UpdateBackupResult(_backupFiles);

                    MessageBox.Show(
                        this,
                        result.GetSummary(),
                        "DLSS Updater",
                        MessageBoxButton.OK,
                        result.Errors.Count == 0
                            ? MessageBoxImage.Information
                            : MessageBoxImage.Warning);
                });
            }
            catch (Exception ex)
            {
                InvokeOnUiThread(() => MessageBox.Show(
                    this,
                    "The update could not be completed." + Environment.NewLine + ex.Message,
                    "DLSS Updater Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error));
            }
            finally
            {
                InvokeOnUiThread(() =>
                {
                    UpdateButton.Content = "Update";
                    bool hasDlssFiles =
                        _dlssFiles.Count + _dlssdFiles.Count + _dlssgFiles.Count > 0;
                    UpdateButton.IsEnabled = hasDlssFiles;
                    UpdateButton.Opacity = hasDlssFiles ? 1.0 : 0.5;
                });
            }
        }

        private async void RestoreBackupButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                "Restore the newest backup for each DLSS file? This will replace the current files.",
                "Restore backup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            SetBackupButtonsEnabled(false);

            try
            {
                BackupOperationResult result = await Task.Run(() =>
                    BackupArchiveService.RestoreLatestBackups(_backupFiles));

                await RefreshScannedFilesAsync(FolderPathTextBox.Text);

                InvokeOnUiThread(() => MessageBox.Show(
                    this,
                    result.GetSummary(true),
                    "Restore backup",
                    MessageBoxButton.OK,
                    result.Errors.Count == 0
                        ? MessageBoxImage.Information
                        : MessageBoxImage.Warning));
            }
            catch (Exception ex)
            {
                InvokeOnUiThread(() => MessageBox.Show(
                    this,
                    "The backup could not be restored." + Environment.NewLine + ex.Message,
                    "Restore backup failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error));
            }
            finally
            {
                InvokeOnUiThread(() => SetBackupButtonsEnabled(_backupFiles.Count > 0));
            }
        }

        private async void DeleteBackupButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBoxResult confirmation = MessageBox.Show(
                this,
                "Delete all DLSS backup files found in the selected folder? This cannot be undone.",
                "Delete backups",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            string folder = FolderPathTextBox.Text;
            SetBackupButtonsEnabled(false);

            try
            {
                BackupOperationResult result = await Task.Run(() =>
                    BackupArchiveService.DeleteBackups(_backupFiles));

                _backupFiles = await Task.Run(() =>
                    BackupScanner.FindBackupFiles(folder));
                InvokeOnUiThread(() =>
                {
                    UpdateBackupResult(_backupFiles);
                    MessageBox.Show(
                        this,
                        result.GetSummary(false),
                        "Delete backups",
                        MessageBoxButton.OK,
                        result.Errors.Count == 0
                            ? MessageBoxImage.Information
                            : MessageBoxImage.Warning);
                });
            }
            catch (Exception ex)
            {
                InvokeOnUiThread(() => MessageBox.Show(
                    this,
                    "The backups could not be deleted." + Environment.NewLine + ex.Message,
                    "Delete backups failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error));
            }
            finally
            {
                InvokeOnUiThread(() => SetBackupButtonsEnabled(_backupFiles.Count > 0));
            }
        }

        private async Task RefreshScannedFilesAsync(string folder)
        {
            Task<List<ScannedFile>> dlssTask = Task.Run(() =>
                FolderScanner.FindFiles(folder, "nvngx_dlss.dll"));
            Task<List<ScannedFile>> dlssdTask = Task.Run(() =>
                FolderScanner.FindFiles(folder, "nvngx_dlssd.dll"));
            Task<List<ScannedFile>> dlssgTask = Task.Run(() =>
                FolderScanner.FindFiles(folder, "nvngx_dlssg.dll"));
            Task<List<ScannedFile>> backupTask = Task.Run(() =>
                BackupScanner.FindBackupFiles(folder));

            await Task.WhenAll(dlssTask, dlssdTask, dlssgTask, backupTask);

            _dlssFiles = await dlssTask;
            _dlssdFiles = await dlssdTask;
            _dlssgFiles = await dlssgTask;
            _backupFiles = await backupTask;

            InvokeOnUiThread(() =>
            {
            UpdateDlssSummary();
            PopulateDlssDetails(folder);
                UpdateBackupResult(_backupFiles);

                bool hasDlssFiles =
                    _dlssFiles.Count + _dlssdFiles.Count + _dlssgFiles.Count > 0;
                UpdateButton.IsEnabled = hasDlssFiles;
                UpdateButton.Opacity = hasDlssFiles ? 1.0 : 0.5;
            });
        }

        private void InvokeOnUiThread(Action action)
        {
            if (Dispatcher.CheckAccess())
            {
                action();
                return;
            }

            Dispatcher.Invoke(action);
        }

        private void SetBackupButtonsEnabled(bool enabled)
        {
            RestoreBackupButton.IsEnabled = enabled;
            DeleteBackupButton.IsEnabled = enabled;
        }

        private void UpdateBackupResult(
            List<ScannedFile> files)
        {
            if (files.Count == 0)
            {
                SetStatus(BackupStatusText, "!  Backup not found", NeutralStatusBrush);
                SetBackupButtonsEnabled(false);
                return;
            }

            if (files.Count == 1)
            {
                SetStatus(
                    BackupStatusText,
                    "✓  Backup found (" + files[0].Version + ")",
                    SuccessStatusBrush);
            }
            else
            {
                SetStatus(
                    BackupStatusText,
                    "✓  " + files.Count + " backups found",
                    SuccessStatusBrush);
            }

            SetBackupButtonsEnabled(true);
        }

        private bool IsRestrictedSearchFolder(
            string folder)
        {
            string fullPath;

            try
            {
                fullPath =
                    System.IO.Path.GetFullPath(folder)
                        .TrimEnd(
                            System.IO.Path.DirectorySeparatorChar,
                            System.IO.Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return true;
            }

            string root =
                System.IO.Path.GetPathRoot(fullPath)
                    .TrimEnd(
                        System.IO.Path.DirectorySeparatorChar,
                        System.IO.Path.AltDirectorySeparatorChar);

            if (string.Equals(
                fullPath,
                root,
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string[] restrictedFolders =
            {
                root + "\\Windows",
                root + "\\Program Files",
                root + "\\Program Files (x86)",
                root + "\\ProgramData"
            };

            foreach (string restrictedFolder in restrictedFolders)
            {
                if (string.Equals(
                    fullPath,
                    restrictedFolder,
                    StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void SetSearchingState()
        {
            SetStatus(DlssStatusText, "Searching...", NeutralStatusBrush);
            ShowDetailsButton.Visibility = Visibility.Collapsed;
        }

        private void UpdateDlssSummary()
        {
            int fileCount =
                _dlssFiles.Count + _dlssdFiles.Count + _dlssgFiles.Count;

            if (fileCount == 0)
            {
                SetStatus(DlssStatusText, "No DLSS files found", NeutralStatusBrush);
                ShowDetailsButton.Visibility = Visibility.Collapsed;
                return;
            }

            SetStatus(
                DlssStatusText,
                "Found " + fileCount + " DLSS file" +
                (fileCount == 1 ? string.Empty : "s"),
                SuccessStatusBrush);
            ShowDetailsButton.Visibility = Visibility.Visible;
        }

        private void SetStatus(
            TextBlock statusText,
            string message,
            Brush foreground)
        {
            statusText.Text = message;
            statusText.Foreground = foreground;
        }

        private void PopulateDlssDetails(string folderPath)
        {
            DetailsFileListPanel.Children.Clear();
            DetailsSummaryText.Text =
                "Found " +
                (_dlssFiles.Count + _dlssdFiles.Count + _dlssgFiles.Count) +
                " supported file(s) in the selected folder and its subfolders.";

            AddDlssDetailRows("DLSS", folderPath, _dlssFiles);
            AddDlssDetailRows("DLSS-D", folderPath, _dlssdFiles);
            AddDlssDetailRows("DLSS-G", folderPath, _dlssgFiles);
        }

        private void AddDlssDetailRows(
            string type,
            string folderPath,
            List<ScannedFile> files)
        {
            foreach (ScannedFile file in files)
            {
                Grid row = new Grid
                {
                    Margin = new Thickness(0, 5, 0, 5)
                };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                AddDetailsCell(row, type, 0);
                AddDetailsCell(row, file.Version, 1);
                AddDetailsCell(row, GetDisplayPath(folderPath, file.Path), 2, true);
                DetailsFileListPanel.Children.Add(row);

                Border separator = new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(Color.FromRgb(229, 229, 229))
                };
                DetailsFileListPanel.Children.Add(separator);
            }
        }

        private void AddDetailsCell(
            Grid row,
            string text,
            int column,
            bool wrap = false)
        {
            TextBlock cell = new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102)),
                TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(cell, column);
            row.Children.Add(cell);
        }

        private void ShowDetailsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            PopulateDlssDetails(FolderPathTextBox.Text);
            MainPage.Visibility = Visibility.Collapsed;
            DlssDetailsPage.Visibility = Visibility.Visible;
        }

        private void BackFromDetailsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            DlssDetailsPage.Visibility = Visibility.Collapsed;
            MainPage.Visibility = Visibility.Visible;
        }

        private void UpdateDlssResult(
            TextBlock icon,
            TextBlock status,
            Button expandButton,
            ScrollViewer fileList,
            StackPanel fileListPanel,
            string folderPath,
            List<ScannedFile> files)
        {
            fileListPanel.Children.Clear();

            fileList.Visibility =
                Visibility.Collapsed;

            if (files.Count == 0)
            {
                icon.Text = "!";
                status.Text = "Not found";

                expandButton.Visibility =
                    Visibility.Collapsed;

                return;
            }

            icon.Text = "✓";

            if (files.Count == 1)
            {
                status.Text = "1 instance found";
            }
            else
            {
                status.Text =
                    files.Count +
                    " instances found";
            }

            foreach (ScannedFile file in files)
            {
                Grid fileRow =
                    new Grid();

                fileRow.Margin =
                    new Thickness(
                        0,
                        4,
                        0,
                        4);

                fileRow.ColumnDefinitions.Add(
                    new ColumnDefinition
                    {
                        Width =
                            new GridLength(85)
                    });

                fileRow.ColumnDefinitions.Add(
                    new ColumnDefinition
                    {
                        Width =
                            new GridLength(
                                1,
                                GridUnitType.Star)
                    });

                TextBlock versionText =
                    new TextBlock();

                versionText.Text =
                    file.Version;

                versionText.FontSize = 13;

                versionText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            102,
                            102,
                            102));

                versionText.VerticalAlignment =
                    VerticalAlignment.Top;

                TextBlock pathText =
                    new TextBlock();

                pathText.Text =
                    GetDisplayPath(
                        folderPath,
                        file.Path);

                pathText.FontSize = 13;

                pathText.Foreground =
                    new SolidColorBrush(
                        Color.FromRgb(
                            102,
                            102,
                            102));

                pathText.TextWrapping =
                    TextWrapping.Wrap;

                pathText.VerticalAlignment =
                    VerticalAlignment.Top;

                Grid.SetColumn(
                    versionText,
                    0);

                Grid.SetColumn(
                    pathText,
                    1);

                fileRow.Children.Add(
                    versionText);

                fileRow.Children.Add(
                    pathText);

                fileListPanel.Children.Add(
                    fileRow);
            }

            expandButton.Visibility =
                Visibility.Visible;
        }

        private string GetDisplayPath(
            string folderPath,
            string filePath)
        {
            try
            {
                string normalizedFolder =
                    System.IO.Path.GetFullPath(folderPath)
                        .TrimEnd(
                            System.IO.Path.DirectorySeparatorChar,
                            System.IO.Path.AltDirectorySeparatorChar);

                string normalizedFile =
                    System.IO.Path.GetFullPath(filePath);

                string prefix =
                    normalizedFolder +
                    System.IO.Path.DirectorySeparatorChar;

                if (normalizedFile.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
                {
                    string relativePath =
                        normalizedFile.Substring(
                            prefix.Length);

                    return ".\\" + relativePath;
                }

                return normalizedFile;
            }
            catch
            {
                return filePath;
            }
        }

        private void ResetFileLists()
        {
            _dlssFiles.Clear();
            _dlssdFiles.Clear();
            _dlssgFiles.Clear();

            DetailsFileListPanel.Children.Clear();
            DetailsSummaryText.Text = string.Empty;
            ShowDetailsButton.Visibility = Visibility.Collapsed;

            DlssFileListPanel.Children.Clear();
            DlssdFileListPanel.Children.Clear();
            DlssgFileListPanel.Children.Clear();

            DlssFileList.Visibility =
                Visibility.Collapsed;

            DlssdFileList.Visibility =
                Visibility.Collapsed;

            DlssgFileList.Visibility =
                Visibility.Collapsed;

            DlssExpandButton.Visibility =
                Visibility.Collapsed;

            DlssdExpandButton.Visibility =
                Visibility.Collapsed;

            DlssgExpandButton.Visibility =
                Visibility.Collapsed;

            DlssExpandButton.Content = "▼";
            DlssdExpandButton.Content = "▼";
            DlssgExpandButton.Content = "▼";
        }

        private void DlssExpandButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ToggleFileList(
                DlssExpandButton,
                DlssFileList);
        }

        private void DlssdExpandButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ToggleFileList(
                DlssdExpandButton,
                DlssdFileList);
        }

        private void DlssgExpandButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ToggleFileList(
                DlssgExpandButton,
                DlssgFileList);
        }

        private void ToggleFileList(
            Button button,
            ScrollViewer fileList)
        {
            if (fileList.Visibility ==
                Visibility.Visible)
            {
                fileList.Visibility =
                    Visibility.Collapsed;

                button.Content = "▼";
            }
            else
            {
                fileList.Visibility =
                    Visibility.Visible;

                button.Content = "▲";
            }
        }
    }
}
