using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Win32;
using SGet.Properties;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;

namespace SGet
{
    /// <summary>
    /// Interaction logic for SGetUserControl.xaml
    /// </summary>
    public partial class SGetUserControl : UserControl
    {
        #region Fields
        
        List<string> propertyNames;
        List<string> propertyValues;
        List<PropertyModel> propertiesList;
        bool trayExit;
        string[] args;

        #endregion

        #region Constructor

        public SGetUserControl()
        {
            InitializeComponent();

            args = Environment.GetCommandLineArgs();
            if (!Settings.Default.ShowWindowOnStartup && args.Length != 2)
            {
                ShowInTaskbar = false;
                Visibility = Visibility.Hidden;
            }

            // Bind DownloadsList to downloadsGrid
            downloadsGrid.ItemsSource = DownloadManager.Instance.DownloadsList;
            DownloadManager.Instance.DownloadsList.CollectionChanged += DownloadsList_CollectionChanged;

            // In case of computer shutdown or restart, save current list of downloads to an XML file
            SystemEvents.SessionEnding += SystemEvents_SessionEnding;

            propertyNames = new List<string>
            {
                "URL",
                "Supports Resume",
                "File Type",
                "Download Folder",
                "Average Speed",
                "Total Time"
            };

            propertyValues = new List<string>();
            propertiesList = new List<PropertyModel>();
            SetEmptyPropertiesGrid();
            propertiesGrid.ItemsSource = propertiesList;

            // Load downloads from the XML file
            LoadDownloadsFromXml();

            if (DownloadManager.Instance.TotalDownloads == 0)
            {
                EnableMenuItems(false);

                // Clean temporary files in the download directory if no downloads were loaded
                if (Directory.Exists(Settings.Default.DownloadLocation))
                {
                    var downloadLocation = new DirectoryInfo(Settings.Default.DownloadLocation);
                    foreach (FileInfo file in downloadLocation.GetFiles())
                    {
                        if (file.FullName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                            file.Delete();
                    }
                }
            }

            cbShowGrid.IsChecked = Settings.Default.ShowGrid;
            cbShowProperties.IsChecked = Settings.Default.ShowProperties;
            cbShowStatusBar.IsChecked = Settings.Default.ShowStatusBar;

            if (cbShowGrid.IsChecked.Value)
            {
                downloadsGrid.GridLinesVisibility = DataGridGridLinesVisibility.All;
            }
            else
            {
                downloadsGrid.GridLinesVisibility = DataGridGridLinesVisibility.None;
            }
            if (cbShowProperties.IsChecked.Value)
            {
                propertiesSplitter.Visibility = System.Windows.Visibility.Visible;
                propertiesPanel.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                propertiesSplitter.Visibility = System.Windows.Visibility.Collapsed;
                propertiesPanel.Visibility = System.Windows.Visibility.Collapsed;
            }
            if (cbShowStatusBar.IsChecked.Value)
            {
                statusBar.Visibility = System.Windows.Visibility.Visible;
            }
            else
            {
                statusBar.Visibility = System.Windows.Visibility.Collapsed;
            }

            trayExit = false;
        }

        #endregion

        #region Methods

        void SetEmptyPropertiesGrid()
        {
            propertiesList.Clear();
            var models = propertyNames.Select(p => new PropertyModel(p, string.Empty)).ToList();
            propertiesList.AddRange(models);
            propertiesGrid.Items.Refresh();
        }

        void PauseAllDownloads()
        {
            if (downloadsGrid.Items.Count > 0)
            {
                foreach (WebDownloadClient download in DownloadManager.Instance.DownloadsList)
                {
                    download.Pause();
                }
            }
        }

        void SaveDownloadsToXml()
        {
            if (DownloadManager.Instance.TotalDownloads > 0)
            {
                // Pause downloads
                PauseAllDownloads();

                var root = new XElement("downloads");

                foreach (WebDownloadClient download in DownloadManager.Instance.DownloadsList)
                {
                    string username = String.Empty;
                    string password = String.Empty;
                    if (download.ServerLogin != null)
                    {
                        username = download.ServerLogin.UserName;
                        password = download.ServerLogin.Password;
                    }

                    var xdl = new XElement("download",
                                        new XElement("file_name", download.FileName),
                                        new XElement("url", download.Url.ToString()),
                                        new XElement("username", username),
                                        new XElement("password", password),
                                        new XElement("temp_path", download.TempDownloadPath),
                                        new XElement("file_size", download.FileSize),
                                        new XElement("downloaded_size", download.DownloadedSize),
                                        new XElement("status", download.Status.ToString()),
                                        new XElement("status_text", download.StatusText),
                                        new XElement("total_time", download.TotalElapsedTime.ToString()),
                                        new XElement("added_on", download.AddedOn.ToString()),
                                        new XElement("completed_on", download.CompletedOn.ToString()),
                                        new XElement("supports_resume", download.SupportsRange.ToString()),
                                        new XElement("has_error", download.HasError.ToString()),
                                        new XElement("open_file", download.OpenFileOnCompletion.ToString()),
                                        new XElement("temp_created", download.TempFileCreated.ToString()),
                                        new XElement("is_batch", download.IsBatch.ToString()),
                                        new XElement("url_checked", download.BatchUrlChecked.ToString()));
                    root.Add(xdl);
                }

                var xd = new XDocument();
                xd.Add(root);
                // Save downloads to XML file
                xd.Save("Downloads.xml");
            }
        }

        void LoadDownloadsFromXml()
        {
            try
            {
                if (File.Exists("Downloads.xml"))
                {
                    // Load downloads from XML file
                    var downloads = XElement.Load("Downloads.xml");
                    if (downloads.HasElements)
                    {
                        IEnumerable<XElement> downloadsList =
                            from el in downloads.Elements()
                            select el;
                        foreach (XElement download in downloadsList)
                        {
                            // Create WebDownloadClient object based on XML data
                            var downloadClient = new WebDownloadClient(download.Element("url").Value)
                            {
                                FileName = download.Element("file_name").Value
                            };

                            downloadClient.DownloadProgressChanged += downloadClient.DownloadProgressChangedHandler;
                            downloadClient.DownloadCompleted += downloadClient.DownloadCompletedHandler;
                            downloadClient.PropertyChanged += PropertyChangedHandler;
                            downloadClient.StatusChanged += StatusChangedHandler;
                            downloadClient.DownloadCompleted += DownloadCompletedHandler;

                            string username = download.Element("username").Value;
                            string password = download.Element("password").Value;
                            if (username != String.Empty && password != String.Empty)
                            {
                                downloadClient.ServerLogin = new NetworkCredential(username, password);
                            }

                            downloadClient.TempDownloadPath = download.Element("temp_path").Value;
                            downloadClient.FileSize = Convert.ToInt64(download.Element("file_size").Value);
                            downloadClient.DownloadedSize = Convert.ToInt64(download.Element("downloaded_size").Value);

                            DownloadManager.Instance.DownloadsList.Add(downloadClient);

                            if (download.Element("status").Value == "Completed")
                            {
                                downloadClient.Status = DownloadStatus.Completed;
                            }
                            else
                            {
                                downloadClient.Status = DownloadStatus.Paused;
                            }

                            downloadClient.StatusText = download.Element("status_text").Value;

                            downloadClient.ElapsedTime = TimeSpan.Parse(download.Element("total_time").Value);
                            downloadClient.AddedOn = DateTime.Parse(download.Element("added_on").Value);
                            downloadClient.CompletedOn = DateTime.Parse(download.Element("completed_on").Value);

                            downloadClient.SupportsRange = Boolean.Parse(download.Element("supports_resume").Value);
                            downloadClient.HasError = Boolean.Parse(download.Element("has_error").Value);
                            downloadClient.OpenFileOnCompletion = Boolean.Parse(download.Element("open_file").Value);
                            downloadClient.TempFileCreated = Boolean.Parse(download.Element("temp_created").Value);
                            downloadClient.IsBatch = Boolean.Parse(download.Element("is_batch").Value);
                            downloadClient.BatchUrlChecked = Boolean.Parse(download.Element("url_checked").Value);

                            if (downloadClient.Status == DownloadStatus.Paused && !downloadClient.HasError && Settings.Default.StartDownloadsOnStartup)
                            {
                                downloadClient.Start();
                            }
                        }

                        // Create empty XML file
                        var root = new XElement("downloads");
                        var xd = new XDocument();
                        xd.Add(root);
                        xd.Save("Downloads.xml");
                    }
                }
            }
            catch (Exception)
            {
                Xceed.Wpf.Toolkit.MessageBox.Show("There was an error while loading the download list.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        void EnableMenuItems(bool enabled)
        {
            btnDelete.IsEnabled = enabled;
            btnClearCompleted.IsEnabled = enabled;
            btnStart.IsEnabled = enabled;
            btnPause.IsEnabled = enabled;
            tcmStartAll.IsEnabled = enabled;
            tcmPauseAll.IsEnabled = enabled;
        }

        void EnableDataGridMenuItems(bool enabled)
        {
            cmStart.IsEnabled = enabled;
            cmPause.IsEnabled = enabled;
            cmDelete.IsEnabled = enabled;
            cmRestart.IsEnabled = enabled;
            cmOpenFile.IsEnabled = enabled;
            cmOpenDownloadFolder.IsEnabled = enabled;
            cmStartAll.IsEnabled = enabled;
            cmPauseAll.IsEnabled = enabled;
            cmSelectAll.IsEnabled = enabled;
            cmCopyURLtoClipboard.IsEnabled = enabled;
        }

        #endregion

        #region Main Window Event Handlers

        void mainWindow_ContentRendered(object sender, EventArgs e)
        {
            // In case the application was started from a web browser and receives command-line arguments
            if (args.Length == 2)
            {
                if (args[1].StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    Clipboard.SetText(args[1]);

                    var newDownloadDialog = new NewDownload(this);
                    newDownloadDialog.ShowDialog();
                }
            }
        }

        void mainWindow_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && Settings.Default.MinimizeToTray)
            {
                ShowInTaskbar = false;
            }
        }

        void mainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (Settings.Default.CloseToTray && !trayExit)
            {
                Hide();
                e.Cancel = true;
                return;
            }

            if (Settings.Default.ConfirmExit)
            {
                string message = "Are you sure you want to exit the application?";
                MessageBoxResult result = Xceed.Wpf.Toolkit.MessageBox.Show(message, "SGet", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    trayExit = false;
                    return;
                }
            }

            SaveDownloadsToXml();
        }

        void SystemEvents_SessionEnding(object sender, SessionEndingEventArgs e)
        {
            SaveDownloadsToXml();
        }

        void mainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl + A selects all downloads in the list
            if ((Keyboard.Modifiers == ModifierKeys.Control) && (e.Key == Key.A))
            {
                downloadsGrid.SelectAll();
            }
        }

        void downloadsGrid_KeyUp(object sender, KeyEventArgs e)
        {
            // Delete key clears selected downloads
            if (e.Key == Key.Delete)
            {
                btnDelete_Click(sender, e);
            }
        }

        void downloadsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (downloadsGrid.SelectedItems.Count > 0)
            {
                foreach (WebDownloadClient downld in DownloadManager.Instance.DownloadsList)
                {
                    downld.IsSelected = false;
                }

                var download = (WebDownloadClient)downloadsGrid.SelectedItem;

                if (propertyValues.Count > 0)
                    propertyValues.Clear();

                propertyValues.Add(download.Url.ToString());
                string resumeSupported = "No";
                if (download.SupportsRange)
                    resumeSupported = "Yes";
                propertyValues.Add(resumeSupported);
                propertyValues.Add(download.FileType);
                propertyValues.Add(download.DownloadFolder);
                propertyValues.Add(download.AverageDownloadSpeed);
                propertyValues.Add(download.TotalElapsedTimeString);

                if (propertiesList.Count > 0)
                    propertiesList.Clear();

                for (int i = 0; i < 6; i++)
                {
                    propertiesList.Add(new PropertyModel(propertyNames[i], propertyValues[i]));
                }

                propertiesGrid.Items.Refresh();
                download.IsSelected = true;
            }
            else
            {
                if (DownloadManager.Instance.TotalDownloads > 0)
                {
                    foreach (WebDownloadClient downld in DownloadManager.Instance.DownloadsList)
                    {
                        downld.IsSelected = false;
                    }
                }
                SetEmptyPropertiesGrid();
            }
        }

        public void PropertyChangedHandler(object sender, PropertyChangedEventArgs e)
        {
            var download = (WebDownloadClient)sender;
            if (e.PropertyName == "AverageSpeedAndTotalTime" && download.Status != DownloadStatus.Deleting)
            {
                Dispatcher.Invoke(new PropertyChangedEventHandler(UpdatePropertiesList), sender, e);
            }
        }

        void UpdatePropertiesList(object sender, PropertyChangedEventArgs e)
        {
            propertyValues.RemoveRange(4, 2);
            var download = (WebDownloadClient)downloadsGrid.SelectedItem;
            propertyValues.Add(download.AverageDownloadSpeed);
            propertyValues.Add(download.TotalElapsedTimeString);

            propertiesList.RemoveRange(4, 2);
            propertiesList.Add(new PropertyModel(propertyNames[4], propertyValues[4]));
            propertiesList.Add(new PropertyModel(propertyNames[5], propertyValues[5]));
            propertiesGrid.Items.Refresh();
        }

        void downloadsGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            dgScrollViewer.ScrollToVerticalOffset(dgScrollViewer.VerticalOffset - e.Delta / 3);
        }

        void propertiesGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            propertiesScrollViewer.ScrollToVerticalOffset(propertiesScrollViewer.VerticalOffset - e.Delta / 3);
        }

        void downloadsGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (DownloadManager.Instance.TotalDownloads == 0)
            {
                EnableDataGridMenuItems(false);
            }
            else
            {
                if (downloadsGrid.SelectedItems.Count == 1)
                {
                    EnableDataGridMenuItems(true);
                }
                else if (downloadsGrid.SelectedItems.Count > 1)
                {
                    EnableDataGridMenuItems(true);
                    cmOpenFile.IsEnabled = false;
                    cmOpenDownloadFolder.IsEnabled = false;
                    cmCopyURLtoClipboard.IsEnabled = false;
                }
                else
                {
                    EnableDataGridMenuItems(false);
                    cmStartAll.IsEnabled = true;
                    cmPauseAll.IsEnabled = true;
                    cmSelectAll.IsEnabled = true;
                }
            }
        }

        void DownloadsList_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (DownloadManager.Instance.TotalDownloads == 1)
            {
                EnableMenuItems(true);
                statusBarDownloads.Content = "1 Download";
            }
            else if (DownloadManager.Instance.TotalDownloads > 1)
            {
                EnableMenuItems(true);
                statusBarDownloads.Content = $"{DownloadManager.Instance.TotalDownloads} Downloads";
            }
            else
            {
                EnableMenuItems(false);
                statusBarDownloads.Content = "Ready";
            }
        }

        public void StatusChangedHandler(object sender, EventArgs e)
        {
            Dispatcher.Invoke(new EventHandler(StatusChanged), sender, e);
        }

        void StatusChanged(object sender, EventArgs e)
        {
            // Start the first download in the queue, if it exists
            var dl = (WebDownloadClient)sender;
            if (dl.Status == DownloadStatus.Paused || dl.Status == DownloadStatus.Completed
                || dl.Status == DownloadStatus.Deleted || dl.HasError)
            {
                foreach (WebDownloadClient d in DownloadManager.Instance.DownloadsList)
                {
                    if (d.Status == DownloadStatus.Queued)
                    {
                        d.Start();
                        break;
                    }
                }
            }

            foreach (WebDownloadClient d in DownloadManager.Instance.DownloadsList)
            {
                if (d.Status == DownloadStatus.Downloading)
                {
                    d.SpeedLimitChanged = true;
                }
            }

            int active = DownloadManager.Instance.ActiveDownloads;
            int completed = DownloadManager.Instance.CompletedDownloads;

            if (active > 0)
            {
                if (completed == 0)
                    statusBarActive.Content = $" ({active} Active)";
                else
                    statusBarActive.Content = $" ({active} Active, ";
            }
            else
                statusBarActive.Content = String.Empty;

            if (completed > 0)
            {
                if (active == 0)
                    statusBarCompleted.Content = $" ({completed} Completed)";
                else
                    statusBarCompleted.Content = $"{completed} Completed)";
            }
            else
                statusBarCompleted.Content = String.Empty;
        }

        public void DownloadCompletedHandler(object sender, EventArgs e)
        {
            if (Settings.Default.ShowBalloonNotification)
            {
                var download = (WebDownloadClient)sender;

                if (download.Status == DownloadStatus.Completed)
                {
                    string title = "Download Completed";
                    string text = $"{download.FileName} has finished downloading.";

                    XNotifyIcon.ShowBalloonTip(title, text, BalloonIcon.Info);
                }
            }
        }

        #endregion

        #region Click Event Handlers

        void btnNewDownload_Click(object sender, RoutedEventArgs e)
        {
            var newDownloadDialog = new NewDownload(this);
            newDownloadDialog.ShowDialog();
        }

        void btnBatchDownload_Click(object sender, RoutedEventArgs e)
        {
            var batchDownloadDialog = new BatchDownload(this);
            batchDownloadDialog.ShowDialog();
        }

        void btnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (downloadsGrid.SelectedItems.Count > 0)
            {
                if (Settings.Default.ConfirmDelete)
                {
                    var result = Xceed.Wpf.Toolkit.MessageBox.Show
                    (
                        "Are you sure you want to delete the selected download(s)?",
                        "Warning",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning
                    );
                    if (result == MessageBoxResult.No)
                        return;
                }

                var selectedDownloads = downloadsGrid.SelectedItems.Cast<WebDownloadClient>();
                var downloadsToDelete = new List<WebDownloadClient>();

                foreach (WebDownloadClient download in selectedDownloads)
                {
                    if (download.HasError || download.Status == DownloadStatus.Paused || download.Status == DownloadStatus.Queued)
                    {
                        if (File.Exists(download.TempDownloadPath))
                        {
                            File.Delete(download.TempDownloadPath);
                        }
                        download.Status = DownloadStatus.Deleting;
                        downloadsToDelete.Add(download);
                    }
                    else if (download.Status == DownloadStatus.Completed)
                    {
                        download.Status = DownloadStatus.Deleting;
                        downloadsToDelete.Add(download);
                    }
                    else
                    {
                        download.Status = DownloadStatus.Deleting;
                        while (true)
                        {
                            if (download.DownloadThread.ThreadState == System.Threading.ThreadState.Stopped)
                            {
                                if (File.Exists(download.TempDownloadPath))
                                {
                                    File.Delete(download.TempDownloadPath);
                                }
                                downloadsToDelete.Add(download);
                                break;
                            }
                        }
                    }
                }

                foreach (var download in downloadsToDelete)
                {
                    download.Status = DownloadStatus.Deleted;
                    DownloadManager.Instance.DownloadsList.Remove(download);
                }
            }
        }

        void btnClearCompleted_Click(object sender, RoutedEventArgs e)
        {
            if (DownloadManager.Instance.TotalDownloads > 0)
            {
                var downloadsToClear = new List<WebDownloadClient>();

                foreach (var download in DownloadManager.Instance.DownloadsList)
                {
                    if (download.Status == DownloadStatus.Completed)
                    {
                        download.Status = DownloadStatus.Deleting;
                        downloadsToClear.Add(download);
                    }
                }

                foreach (var download in downloadsToClear)
                {
                    download.Status = DownloadStatus.Deleted;
                    DownloadManager.Instance.DownloadsList.Remove(download);
                }
            }
        }

        void btnStart_Click(object sender, RoutedEventArgs e)
        {
            if (downloadsGrid.SelectedItems.Count > 0)
            {
                var selectedDownloads = downloadsGrid.SelectedItems.Cast<WebDownloadClient>();

                foreach (WebDownloadClient download in selectedDownloads)
                {
                    if (download.Status == DownloadStatus.Paused || download.HasError)
                    {
                        download.Start();
                    }
                }
            }
        }

        void btnPause_Click(object sender, RoutedEventArgs e)
        {
            if (downloadsGrid.SelectedItems.Count > 0)
            {
                var selectedDownloads = downloadsGrid.SelectedItems.Cast<WebDownloadClient>();

                foreach (WebDownloadClient download in selectedDownloads)
                {
                    download.Pause();
                }
            }
        }

        void btnAbout_Click(object sender, RoutedEventArgs e)
        {
            new About().ShowDialog();
        }

        void cmRestart_Click(object sender, RoutedEventArgs e)
        {
            if (downloadsGrid.SelectedItems.Count > 0)
            {
                var selectedDownloads = downloadsGrid.SelectedItems.Cast<WebDownloadClient>();

                foreach (WebDownloadClient download in selectedDownloads)
                {
                    download.Restart();
                }
            }
        }

        void cmOpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (downloadsGrid.SelectedItems.Count == 1)
            {
                var download = (WebDownloadClient)downloadsGrid.SelectedItem;
                if (download.Status == DownloadStatus.Completed && File.Exists(download.DownloadPath))
                {
                    Process.Start(@download.DownloadPath);
                }
            }
        }

        void cmOpenDownloadFolder_Click(object sender, RoutedEventArgs e)
        {
            if (downloadsGrid.SelectedItems.Count == 1)
            {
                var download = (WebDownloadClient)downloadsGrid.SelectedItem;
                int lastIndex = download.DownloadPath.LastIndexOf("\\", StringComparison.Ordinal);
                string directory = download.DownloadPath.Remove(lastIndex + 1);
                if (Directory.Exists(directory))
                {
                    Process.Start(@directory);
                }
            }
        }

        void cmStartAll_Click(object sender, RoutedEventArgs e)
        {
            if (downloadsGrid.Items.Count > 0)
            {
                foreach (WebDownloadClient download in DownloadManager.Instance.DownloadsList)
                {
                    if (download.Status == DownloadStatus.Paused || download.HasError)
                    {
                        download.Start();
                    }
                }
            }
        }

        void cmPauseAll_Click(object sender, RoutedEventArgs e)
        {
            PauseAllDownloads();
        }

        void cmSelectAll_Click(object sender, RoutedEventArgs e)
        {
            if (downloadsGrid.Items.Count > 0)
            {
                if (downloadsGrid.SelectedItems.Count < downloadsGrid.Items.Count)
                {
                    downloadsGrid.SelectAll();
                }
            }
        }

        void cmCopyURLtoClipboard_Click(object sender, RoutedEventArgs e)
        {
            if (downloadsGrid.SelectedItems.Count == 1)
            {
                var download = (WebDownloadClient)downloadsGrid.SelectedItem;
                Clipboard.SetText(download.Url.ToString());
            }
        }

        void tcmShowMainWindow_Click(object sender, RoutedEventArgs e)
        {
            ShowInTaskbar = true;
            Visibility = Visibility.Visible;
            WindowState = System.Windows.WindowState.Normal;
        }

        void tcmExit_Click(object sender, RoutedEventArgs e)
        {
            // Close all windows
            trayExit = true;
            for (int intCounter = App.Current.Windows.Count - 1; intCounter >= 0; intCounter--)
                App.Current.Windows[intCounter].Close();
        }

        void btnSetLimits_Click(object sender, RoutedEventArgs e)
        {
            new Preferences(true).ShowDialog();
        }

        void btnPreferences_Click(object sender, RoutedEventArgs e)
        {
            new Preferences(false).ShowDialog();
        }

        void cbShowGrid_Click(object sender, RoutedEventArgs e)
        {
            if (cbShowGrid.IsChecked.Value)
            {
                downloadsGrid.GridLinesVisibility = DataGridGridLinesVisibility.All;
            }
            else
            {
                downloadsGrid.GridLinesVisibility = DataGridGridLinesVisibility.None;
            }
            Settings.Default.ShowGrid = cbShowGrid.IsChecked.Value;
            Settings.Default.Save();
        }

        void cbShowProperties_Click(object sender, RoutedEventArgs e)
        {
            if (cbShowProperties.IsChecked.Value)
            {
                propertiesSplitter.Visibility = Visibility.Visible;
                propertiesPanel.Visibility = Visibility.Visible;
            }
            else
            {
                propertiesSplitter.Visibility = Visibility.Collapsed;
                propertiesPanel.Visibility = Visibility.Collapsed;
            }
            Settings.Default.ShowProperties = cbShowProperties.IsChecked.Value;
            Settings.Default.Save();
        }

        void cbShowStatusBar_Click(object sender, RoutedEventArgs e)
        {
            if (cbShowStatusBar.IsChecked.Value)
            {
                statusBar.Visibility = Visibility.Visible;
            }
            else
            {
                statusBar.Visibility = Visibility.Collapsed;
            }
            Settings.Default.ShowStatusBar = cbShowStatusBar.IsChecked.Value;
            Settings.Default.Save();
        }

        #endregion

    }
}
