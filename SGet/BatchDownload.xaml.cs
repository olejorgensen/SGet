using SGet.Core;
using SGet.Core.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;

namespace SGet
{
    public partial class BatchDownload : Window
    {
        bool batchUrlValid;
        bool startImmediately;
        MainWindow mainWindow;
        List<string> downloadUrls = new List<string>();

        #region Constructor

        public BatchDownload(MainWindow mainWin)
        {
            InitializeComponent();
            mainWindow = mainWin;
            tbDownloadFolder.Text = Settings.Default.DownloadLocation;
            batchUrlValid = false;
            startImmediately = true;
            rbFrom1.IsChecked = true;

            listBoxFiles.ItemsSource = downloadUrls;

            if (System.Windows.Clipboard.ContainsText())
            {
                string clipboardText = System.Windows.Clipboard.GetText();

                if (IsBatchUrlValid(clipboardText))
                {
                    batchUrlValid = true;
                    tbURL.Text = clipboardText;
                }
            }
        }

        #endregion

        #region Methods

        // Validate the Batch URL
        bool IsBatchUrlValid(string Url)
        {
            if (Url.StartsWith("http", StringComparison.OrdinalIgnoreCase) && Url.Contains(":") && (Url.Length > 15) && (Utilities.CountOccurence(Url, '/') >= 3)
                 && (Url.LastIndexOf('/') != Url.Length - 1) && (Utilities.CountOccurence(Url, '*') == 1))
            {
                string lastChars = Url.Substring(Url.Length - 9);

                // Check if the URL contains a dot in the last 8 characters
                if (lastChars.Contains(".") && (lastChars.LastIndexOf('.') != lastChars.Length - 1))
                {
                    // Get the extension string based on the index of the last dot
                    string ext = lastChars.Substring(lastChars.LastIndexOf('.') + 1);

                    // Check if the extension string contains some illegal characters
                    string chars = " ?#&%=[]_-+~:;\\/!$<>\"\'*";

                    foreach (char c in ext)
                    {
                        foreach (char s in chars)
                        {
                            if (c == s)
                                return false;
                        }
                    }

                    return true;
                }
                return false;
            }
            return false;
        }

        // Update list of files to download
        void UpdateFilesList()
        {
            if (batchUrlValid)
            {
                if (rbFrom1.IsChecked.Value)
                {
                    if ((tbFrom1.Text.Length == 0) || (tbTo1.Text.Length == 0) || (Convert.ToInt32(tbFrom1.Text) > Convert.ToInt32(tbTo1.Text)))
                    {
                        downloadUrls.Clear();
                        listBoxFiles.Items.Refresh();
                        lblFilesToDownload.Content = "0 files to download";
                        return;
                    }
                }

                if (rbFrom2.IsChecked.Value)
                {
                    if ((tbFrom2.Text.Length == 0) || (tbTo2.Text.Length == 0) || Convert.ToChar(tbFrom2.Text) > Convert.ToChar(tbTo2.Text))
                    {
                        downloadUrls.Clear();
                        listBoxFiles.Items.Refresh();
                        lblFilesToDownload.Content = "0 files to download";
                        return;
                    }
                }

                string batchUrl = tbURL.Text;

                downloadUrls.Clear();

                if (rbFrom1.IsChecked.Value)
                {
                    int firstNumber = Convert.ToInt32(tbFrom1.Text);
                    int lastNumber = Convert.ToInt32(tbTo1.Text);
                    int numberLength = intNumberLength.Value.Value;

                    for (int i = firstNumber; i <= lastNumber; i++)
                    {
                        string num = String.Empty;

                        if (i.ToString().Length < numberLength)
                        {
                            int diff = numberLength - i.ToString().Length;
                            string zeros = String.Empty;
                            for (int n = 0; n < diff; n++)
                            {
                                zeros += "0";
                            }
                            num = zeros + i.ToString();
                        }
                        else
                        {
                            num = i.ToString();
                        }

                        string url = batchUrl.Replace("*", num);
                        downloadUrls.Add(url);
                    }
                }
                else
                {
                    int firstChar = Convert.ToChar(tbFrom2.Text);
                    int lastChar = Convert.ToChar(tbTo2.Text);
                    for (int c = firstChar; c <= lastChar; c++)
                    {
                        if (Char.IsLetter((char)c))
                        {
                            string url = batchUrl.Replace('*', (char)c);
                            downloadUrls.Add(url);
                        }
                    }
                }

                listBoxFiles.Items.Refresh();
                if (downloadUrls.Count == 1)
                    lblFilesToDownload.Content = "1 file to download";
                else
                    lblFilesToDownload.Content = downloadUrls.Count + " files to download";
            }
            else
            {
                if (downloadUrls.Count > 0)
                {
                    downloadUrls.Clear();
                    listBoxFiles.Items.Refresh();
                    lblFilesToDownload.Content = "0 files to download";
                }
            }
        }

        #endregion

        #region Event Handlers

        void btnDownload_Click(object sender, RoutedEventArgs e)
        {
            if (downloadUrls.Count > 0)
            {
                try
                {
                    foreach (string url in downloadUrls)
                    {
                        var download = new WebDownloadClient(url)
                        {
                            IsBatch = true,
                            BatchUrlChecked = false,
                            FileName = url.Substring(url.LastIndexOf('/') + 1)
                        };

                        // Register WebDownloadClient events
                        download.DownloadProgressChanged += download.DownloadProgressChangedHandler;
                        download.DownloadCompleted += download.DownloadCompletedHandler;
                        download.PropertyChanged += mainWindow.PropertyChangedHandler;
                        download.StatusChanged += mainWindow.StatusChangedHandler;
                        download.DownloadCompleted += mainWindow.DownloadCompletedHandler;

                        // Create path to temporary file
                        if (!Directory.Exists(tbDownloadFolder.Text))
                        {
                            Directory.CreateDirectory(tbDownloadFolder.Text);
                        }
                        string filePath = Path.Combine(tbDownloadFolder.Text, download.FileName);
                        string tempPath = filePath + ".tmp";

                        // Check if there is already an ongoing download on that path
                        if (File.Exists(tempPath))
                        {
                            // If there is, skip adding this download
                            continue;
                        }

                        // Check if the file already exists
                        if (File.Exists(filePath))
                        {
                            string message = "There is already a file with the same name, do you want to overwrite it?";
                            MessageBoxResult result = Xceed.Wpf.Toolkit.MessageBox.Show(message, "File Name Conflict: " + filePath, MessageBoxButton.YesNo, MessageBoxImage.Warning);

                            if (result == MessageBoxResult.Yes)
                            {
                                File.Delete(filePath);
                            }
                            else
                            {
                                // Skip adding this download
                                continue;
                            }
                        }

                        // Set username and password if HTTP authentication is required
                        if (cbLoginToServer.IsChecked.Value && (tbUsername.Text.Trim().Length > 0) && (tbPassword.Password.Trim().Length > 0))
                        {
                            download.ServerLogin = new NetworkCredential(tbUsername.Text.Trim(), tbPassword.Password.Trim());
                        }

                        download.TempDownloadPath = tempPath;

                        download.AddedOn = DateTime.UtcNow;
                        download.CompletedOn = DateTime.MinValue;

                        // Add the download to the downloads list
                        DownloadManager.Instance.DownloadsList.Add(download);

                        // Start downloading the file
                        if (startImmediately)
                            download.Start();
                        else
                            download.Status = DownloadStatus.Paused;
                    }

                    // Close the Create Batch Download window
                    Close();
                }
                catch (Exception ex)
                {
                    Xceed.Wpf.Toolkit.MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
                Xceed.Wpf.Toolkit.MessageBox.Show("There are no files to download!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var fbDialog = new FolderBrowserDialog
            {
                Description = "Choose Batch Download Folder",
                ShowNewFolderButton = true
            };
            DialogResult result = fbDialog.ShowDialog();

            if (result.ToString().Equals("OK"))
            {
                string path = fbDialog.SelectedPath;
                if (path.EndsWith("\\", StringComparison.Ordinal) == false)
                    path += "\\";
                tbDownloadFolder.Text = path;
            }
        }

        void tbURL_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsBatchUrlValid(tbURL.Text))
            {
                batchUrlValid = true;
                UpdateFilesList();
            }
            else
            {
                batchUrlValid = false;
                UpdateFilesList();
            }
        }

        void cbStartImmediately_Click(object sender, RoutedEventArgs e)
        {
            startImmediately = cbStartImmediately.IsChecked.Value;
        }

        void cbLoginToServer_Click(object sender, RoutedEventArgs e)
        {
            tbUsername.IsEnabled = cbLoginToServer.IsChecked.Value;
            tbPassword.IsEnabled = cbLoginToServer.IsChecked.Value;
        }

        void rbFrom1_Checked(object sender, RoutedEventArgs e)
        {
            tbFrom1.IsEnabled = true;
            tbTo1.IsEnabled = true;
            intNumberLength.IsEnabled = true;
            tbFrom2.IsEnabled = false;
            tbTo2.IsEnabled = false;
            UpdateFilesList();
        }

        void rbFrom2_Checked(object sender, RoutedEventArgs e)
        {
            tbFrom2.IsEnabled = true;
            tbTo2.IsEnabled = true;
            tbFrom1.IsEnabled = false;
            tbTo1.IsEnabled = false;
            intNumberLength.IsEnabled = false;
            UpdateFilesList();
        }

        void tbFrom1_TextChanged(object sender, TextChangedEventArgs e)
        {
            for (int i = 0; i < tbFrom1.Text.Length; i++)
            {
                if (!char.IsDigit(tbFrom1.Text[i]))
                    tbFrom1.Text = String.Empty;
            }
            UpdateFilesList();
        }

        void tbTo1_TextChanged(object sender, TextChangedEventArgs e)
        {
            for (int i = 0; i < tbTo1.Text.Length; i++)
            {
                if (!char.IsDigit(tbTo1.Text[i]))
                    tbTo1.Text = String.Empty;
            }
            UpdateFilesList();
        }

        void tbFrom2_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (tbFrom2.Text.Length == 1)
            {
                if (!char.IsLetter(tbFrom2.Text[0]))
                    tbFrom2.Text = "a";
            }
            UpdateFilesList();
        }

        void tbTo2_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (tbTo2.Text.Length == 1)
            {
                if (!char.IsLetter(tbTo2.Text[0]))
                    tbTo2.Text = "z";
            }
            UpdateFilesList();
        }

        void intNumberLength_ValueChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            UpdateFilesList();
        }

        #endregion
    }
}
