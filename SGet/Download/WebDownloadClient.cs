using SGet.Properties;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows;

namespace SGet
{
    public class WebDownloadClient : INotifyPropertyChanged
    {
        #region Fields and Properties

        // File name
        public string FileName { get; set; }

        // URL of the file to download
        public Uri Url { get; private set; }

        // File type (extension)
        public string FileType
        {
            get
            {
                return Url.ToString().Substring(Url.ToString().LastIndexOf('.') + 1).ToUpper();
            }
        }

        // Username and password for accessing the HTTP server
        public NetworkCredential ServerLogin;

        // HTTP proxy server information
        public WebProxy Proxy;

        // Thread for the download process
        public Thread DownloadThread;

        // Temporary file path
        public string TempDownloadPath { get; set; }

        // Downloaded file path
        public string DownloadPath
        {
            get
            {
                return TempDownloadPath.Remove(TempDownloadPath.Length - 4);
            }
        }

        // Local folder which contains the file
        public string DownloadFolder
        {
            get
            {
                return TempDownloadPath.Remove(TempDownloadPath.LastIndexOf("\\", StringComparison.Ordinal) + 1);
            }
        }

        // File size (in bytes)
        public long FileSize { get; set; }
        public string FileSizeString
        {
            get
            {
                return DownloadManager.FormatSizeString(FileSize);
            }
        }

        // Size of downloaded data which was written to the local file
        public long DownloadedSize { get; set; }
        public string DownloadedSizeString
        {
            get
            {
                return DownloadManager.FormatSizeString(DownloadedSize + CachedSize);
            }
        }

        // Percentage of downloaded data
        public float Percent
        {
            get
            {
                return (DownloadedSize + CachedSize) / (float)FileSize * 100F;
            }
        }

        public string PercentString
        {
            get
            {
                if (Percent < 0 || float.IsNaN(Percent))
                    return "0.0%";
                if (Percent > 100)
                    return "100.0%";
                return String.Format(numberFormat, "{0:0.0}%", Percent);
            }
        }

        // Progress bar value
        public float Progress
        {
            get
            {
                return Percent;
            }
        }

        // Download speed
        public int downloadSpeed;
        public string DownloadSpeed
        {
            get
            {
                if (Status == DownloadStatus.Downloading && !HasError)
                {
                    return DownloadManager.FormatSpeedString(downloadSpeed);
                }
                return String.Empty;
            }
        }

        // Used for updating download speed on the DataGrid
        int speedUpdateCount;

        // Average download speed
        public string AverageDownloadSpeed
        {
            get
            {
                return DownloadManager.FormatSpeedString((int)Math.Floor((DownloadedSize + CachedSize) / TotalElapsedTime.TotalSeconds));
            }
        }

        // List of download speed values in the last 10 seconds
        List<int> downloadRates = new List<int>();

        // Average download speed in the last 10 seconds, used for calculating the time left to complete the download
        int recentAverageRate;

        // Time left to complete the download
        public string TimeLeft
        {
            get
            {
                if (recentAverageRate > 0 && Status == DownloadStatus.Downloading && !HasError)
                {
                    double secondsLeft = (FileSize - DownloadedSize + CachedSize) / recentAverageRate;

                    var span = TimeSpan.FromSeconds(secondsLeft);

                    return DownloadManager.FormatTimeSpanString(span);
                }
                return String.Empty;
            }
        }

        // Download status
        DownloadStatus status;
        public DownloadStatus Status
        {
            get
            {
                return status;
            }
            set
            {
                status = value;
                if (status != DownloadStatus.Deleting)
                    RaiseStatusChanged();
            }
        }

        // Status text in the DataGrid
        public string StatusText;
        public string StatusString
        {
            get
            {
                if (HasError)
                    return StatusText;
                return Status.ToString();
            }
            set
            {
                StatusText = value;
                RaiseStatusChanged();
            }
        }

        // Elapsed time (doesn't include the time period when the download was paused)
        public TimeSpan ElapsedTime = new TimeSpan();

        // Time when the download was last started
        DateTime lastStartTime;

        // Total elapsed time (includes the time period when the download was paused)
        public TimeSpan TotalElapsedTime
        {
            get
            {
                if (Status != DownloadStatus.Downloading)
                {
                    return ElapsedTime;
                }
                return ElapsedTime.Add(DateTime.UtcNow - lastStartTime);
            }
        }

        public string TotalElapsedTimeString
        {
            get
            {
                return DownloadManager.FormatTimeSpanString(TotalElapsedTime);
            }
        }

        // Time and size of downloaded data in the last calculaction of download speed
        DateTime lastNotificationTime;
        long lastNotificationDownloadedSize;

        // Last update time of the DataGrid item
        public DateTime LastUpdateTime { get; set; }

        // Date and time when the download was added to the list
        public DateTime AddedOn { get; set; }

        private readonly string DateFormat = "dd.MM.yyyy. HH:mm:ss";
        public string AddedOnString
        {
            get
            {
                return AddedOn.ToString(DateFormat);
            }
        }

        // Date and time when the download was completed
        public DateTime CompletedOn { get; set; }
        public string CompletedOnString
        {
            get
            {
                if (CompletedOn != DateTime.MinValue)
                {
                    return CompletedOn.ToString(DateFormat);
                }
                return String.Empty;
            }
        }

        // Server supports the Range header (resuming the download)
        public bool SupportsRange { get; set; }

        // There was an error during download
        public bool HasError { get; set; }

        // Open file as soon as the download is completed
        public bool OpenFileOnCompletion { get; set; }

        // Temporary file was created
        public bool TempFileCreated { get; set; }

        // Download is selected in the DataGrid
        public bool IsSelected { get; set; }

        // Download is part of a batch
        public bool IsBatch { get; set; }

        // Batch URL was checked
        public bool BatchUrlChecked { get; set; }

        // Speed limit was changed
        public bool SpeedLimitChanged { get; set; }

        // Download buffer count per notification (DownloadProgressChanged event)
        public int BufferCountPerNotification { get; set; }

        // Buffer size
        public int BufferSize { get; set; }

        // Size of downloaded data in the cache memory
        public int CachedSize { get; set; }

        // Maxiumum cache size
        public int MaxCacheSize { get; set; }

        // Number format with a dot as the decimal separator
        NumberFormatInfo numberFormat = NumberFormatInfo.InvariantInfo;

        // Used for blocking other processes when a file is being created or written to
        static object fileLocker = new object();

        #endregion

        #region Constructor and Events

        public WebDownloadClient(string url)
        {
            BufferSize = 1024; // Buffer size is 1KB
            MaxCacheSize = Settings.Default.MemoryCacheSize * 1024; // Default cache size is 1MB
            BufferCountPerNotification = 64;

            Url = new Uri(url, UriKind.Absolute);

            SupportsRange = false;
            HasError = false;
            OpenFileOnCompletion = false;
            TempFileCreated = false;
            IsSelected = false;
            IsBatch = false;
            BatchUrlChecked = false;
            SpeedLimitChanged = false;
            speedUpdateCount = 0;
            recentAverageRate = 0;
            StatusText = String.Empty;

            Status = DownloadStatus.Initialized;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public event EventHandler StatusChanged;

        public event EventHandler DownloadProgressChanged;

        public event EventHandler DownloadCompleted;

        #endregion

        #region Event Handlers

        // Generate PropertyChanged event to update the UI
        protected void RaisePropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // Generate StatusChanged event
        protected virtual void RaiseStatusChanged()
        {
            StatusChanged?.Invoke(this, EventArgs.Empty);
        }

        // Generate DownloadProgressChanged event
        protected virtual void RaiseDownloadProgressChanged()
        {
            DownloadProgressChanged?.Invoke(this, EventArgs.Empty);
        }

        // Generate DownloadCompleted event
        protected virtual void RaiseDownloadCompleted()
        {
            DownloadCompleted?.Invoke(this, EventArgs.Empty);
        }

        // DownloadProgressChanged event handler
        public void DownloadProgressChangedHandler(object sender, EventArgs e)
        {
            // Update the UI every second
            if (DateTime.UtcNow > LastUpdateTime.AddSeconds(1))
            {
                CalculateDownloadSpeed();
                CalculateAverageRate();
                UpdateDownloadDisplay();
                LastUpdateTime = DateTime.UtcNow;
            }
        }

        // DownloadCompleted event handler
        public void DownloadCompletedHandler(object sender, EventArgs e)
        {
            if (!HasError)
            {
                // If the file already exists, delete it
                if (File.Exists(DownloadPath))
                {
                    File.Delete(DownloadPath);
                }

                // Convert the temporary (.tmp) file to the actual (requested) file
                if (File.Exists(TempDownloadPath))
                {
                    File.Move(TempDownloadPath, DownloadPath);
                }

                Status = DownloadStatus.Completed;
                UpdateDownloadDisplay();

                if (OpenFileOnCompletion && File.Exists(DownloadPath))
                {
                    Process.Start(@DownloadPath);
                }
            }
            else
            {
                Status = DownloadStatus.Error;
                UpdateDownloadDisplay();
            }
        }

        #endregion

        #region Methods

        bool FindHeader(string[] headers, string header)
        {
            var found = headers
                .Where(h => h != null)
                .FirstOrDefault(h => h.Equals(header, StringComparison.OrdinalIgnoreCase)) != null;
            return found;
        }

        // Check URL to get file size, set login and/or proxy server information, check if the server supports the Range header
        public void CheckUrl()
        {
            try
            {
                var webRequest = CreateWebRequest("HEAD", 5000);
                using (WebResponse response = webRequest.GetResponse())
                {
                    SupportsRange = FindHeader(response.Headers.AllKeys, "Accept-Ranges");
                    FileSize = response.ContentLength;
                    if (FileSize <= 0)
                    {
                        Xceed.Wpf.Toolkit.MessageBox.Show
                        (
                            "The requested file does not exist!",
                            "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error
                        );
                        HasError = true;
                    }
                }
            }
            catch (Exception)
            {
                Xceed.Wpf.Toolkit.MessageBox.Show
                (
                    "There was an error while getting the file information. Please make sure the URL is accessible.",
                    "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error
                );
                HasError = true;
            }
        }

        // Batch download URL check
        void CheckBatchUrl()
        {
            var webRequest = CreateWebRequest("HEAD", 5000);
            using (WebResponse response = webRequest.GetResponse())
            {
                SupportsRange = FindHeader(response.Headers.AllKeys, "Accept-Ranges");
                FileSize = response.ContentLength;
                if (FileSize <= 0)
                {
                    StatusString = "Error: The requested file does not exist";
                    FileSize = 0;
                    HasError = true;
                }
                RaisePropertyChanged("FileSizeString");
            }
        }

        // Create temporary file
        void CreateTempFile()
        {
            // Lock this block of code so other threads and processes don't interfere with file creation
            lock (fileLocker)
            {
                using (FileStream fileStream = File.Create(TempDownloadPath))
                {
                    long createdSize = 0;
                    byte[] buffer = new byte[4096];
                    while (createdSize < FileSize)
                    {
                        int bufferSize = (FileSize - createdSize) < 4096
                            ? (int)(FileSize - createdSize) : 4096;
                        fileStream.Write(buffer, 0, bufferSize);
                        createdSize += bufferSize;
                    }
                }
            }
        }

        // Write data from the cache to the temporary file
        void WriteCacheToFile(MemoryStream downloadCache, int cachedSize)
        {
            // Block other threads and processes from using the file
            lock (fileLocker)
            {
                using (var fileStream = new FileStream(TempDownloadPath, FileMode.Open))
                {
                    byte[] cacheContent = new byte[cachedSize];
                    downloadCache.Seek(0, SeekOrigin.Begin);
                    downloadCache.Read(cacheContent, 0, cachedSize);
                    fileStream.Seek(DownloadedSize, SeekOrigin.Begin);
                    fileStream.Write(cacheContent, 0, cachedSize);
                }
            }
        }

        // Calculate download speed
        void CalculateDownloadSpeed()
        {
            DateTime now = DateTime.UtcNow;
            TimeSpan interval = now - lastNotificationTime;
            double timeDiff = interval.TotalSeconds;
            double sizeDiff = DownloadedSize + CachedSize - lastNotificationDownloadedSize;

            downloadSpeed = (int)Math.Floor(sizeDiff / timeDiff);

            downloadRates.Add(downloadSpeed);

            lastNotificationDownloadedSize = DownloadedSize + CachedSize;
            lastNotificationTime = now;
        }

        // Calculate average download speed in the last 10 seconds
        void CalculateAverageRate()
        {
            if (downloadRates.Count > 0)
            {
                if (downloadRates.Count > 10)
                    downloadRates.RemoveAt(0);

                int rateSum = 0;
                recentAverageRate = 0;
                foreach (int rate in downloadRates)
                {
                    rateSum += rate;
                }

                recentAverageRate = rateSum / downloadRates.Count;
            }
        }

        // Update download display (on downloadsGrid and propertiesGrid controls)
        void UpdateDownloadDisplay()
        {
            RaisePropertyChanged(nameof(DownloadedSizeString));
            RaisePropertyChanged(nameof(PercentString));
            RaisePropertyChanged(nameof(Progress));

            // New download speed update every 4 seconds
            TimeSpan startInterval = DateTime.UtcNow - lastStartTime;
            if (speedUpdateCount == 0 || startInterval.TotalSeconds < 4 || HasError || Status == DownloadStatus.Paused
                || Status == DownloadStatus.Queued || Status == DownloadStatus.Completed)
            {
                RaisePropertyChanged(nameof(DownloadSpeed));
            }
            speedUpdateCount++;
            if (speedUpdateCount == 4)
                speedUpdateCount = 0;

            RaisePropertyChanged(nameof(TimeLeft));
            RaisePropertyChanged(nameof(StatusString));
            RaisePropertyChanged(nameof(CompletedOnString));

            if (IsSelected)
            {
                RaisePropertyChanged("AverageSpeedAndTotalTime");
            }
        }

        // Reset download properties to default values
        void ResetProperties()
        {
            HasError = false;
            TempFileCreated = false;
            DownloadedSize = 0;
            CachedSize = 0;
            speedUpdateCount = 0;
            recentAverageRate = 0;
            downloadRates.Clear();
            ElapsedTime = new TimeSpan();
            CompletedOn = DateTime.MinValue;
        }

        // Start or continue download
        public void Start()
        {
            if (Status == DownloadStatus.Initialized || Status == DownloadStatus.Paused
                || Status == DownloadStatus.Queued || HasError)
            {
                if (!SupportsRange && DownloadedSize > 0)
                {
                    StatusString = "Error: Server does not support resume";
                    HasError = true;
                    RaiseDownloadCompleted();
                    return;
                }

                HasError = false;
                Status = DownloadStatus.Waiting;
                RaisePropertyChanged("StatusString");

                if (DownloadManager.Instance.ActiveDownloads > Settings.Default.MaxDownloads)
                {
                    Status = DownloadStatus.Queued;
                    RaisePropertyChanged("StatusString");
                    return;
                }

                // Start the download thread
                DownloadThread = new Thread(new ThreadStart(DownloadFile))
                {
                    IsBackground = true
                };
                DownloadThread.Start();
            }
        }

        // Pause download
        public void Pause()
        {
            if (Status == DownloadStatus.Waiting || Status == DownloadStatus.Downloading)
            {
                Status = DownloadStatus.Pausing;
            }
            if (Status == DownloadStatus.Queued)
            {
                Status = DownloadStatus.Paused;
                RaisePropertyChanged("StatusString");
            }
        }

        // Restart download
        public void Restart()
        {
            if (HasError || Status == DownloadStatus.Completed)
            {
                if (File.Exists(TempDownloadPath))
                {
                    File.Delete(TempDownloadPath);
                }
                if (File.Exists(DownloadPath))
                {
                    File.Delete(DownloadPath);
                }

                ResetProperties();
                Status = DownloadStatus.Waiting;
                UpdateDownloadDisplay();

                if (DownloadManager.Instance.ActiveDownloads > Settings.Default.MaxDownloads)
                {
                    Status = DownloadStatus.Queued;
                    RaisePropertyChanged("StatusString");
                    return;
                }

                DownloadThread = new Thread(new ThreadStart(DownloadFile))
                {
                    IsBackground = true
                };
                DownloadThread.Start();
            }
        }

        // Download file bytes from the HTTP response stream
        void DownloadFile()
        {
            HttpWebRequest webRequest = null;
            HttpWebResponse webResponse = null;
            Stream responseStream = null;
            ThrottledStream throttledStream = null;
            MemoryStream downloadCache = null;
            speedUpdateCount = 0;
            recentAverageRate = 0;
            if (downloadRates.Count > 0)
                downloadRates.Clear();

            try
            {
                if (IsBatch && !BatchUrlChecked)
                {
                    CheckBatchUrl();
                    if (HasError)
                    {
                        RaiseDownloadCompleted();
                        return;
                    }
                    BatchUrlChecked = true;
                }

                if (!TempFileCreated)
                {
                    // Reserve local disk space for the file
                    CreateTempFile();
                    TempFileCreated = true;
                }

                lastStartTime = DateTime.UtcNow;

                if (Status == DownloadStatus.Waiting)
                    Status = DownloadStatus.Downloading;

                // Create request to the server to download the file
                webRequest = (HttpWebRequest)WebRequest.Create(Url);
                webRequest.Method = "GET";

                if (ServerLogin != null)
                {
                    webRequest.PreAuthenticate = true;
                    webRequest.Credentials = ServerLogin;
                }
                else
                {
                    webRequest.Credentials = CredentialCache.DefaultCredentials;
                }

                if (Proxy != null)
                {
                    webRequest.Proxy = Proxy;
                }
                else
                {
                    webRequest.Proxy = WebRequest.DefaultWebProxy;
                }

                // Set download starting point
                webRequest.AddRange(DownloadedSize);

                // Get response from the server and the response stream
                webResponse = (HttpWebResponse)webRequest.GetResponse();
                responseStream = webResponse.GetResponseStream();

                // Set a 5 second timeout, in case of internet connection break
                responseStream.ReadTimeout = 5000;

                // Set speed limit
                long maxBytesPerSecond = 0;
                if (Settings.Default.EnableSpeedLimit)
                {
                    maxBytesPerSecond = (Settings.Default.SpeedLimit * 1024) / DownloadManager.Instance.ActiveDownloads;
                }
                else
                {
                    maxBytesPerSecond = ThrottledStream.Infinite;
                }
                throttledStream = new ThrottledStream(responseStream, maxBytesPerSecond);

                // Create memory cache with the specified size
                downloadCache = new MemoryStream(MaxCacheSize);

                // Create 1KB buffer
                byte[] downloadBuffer = new byte[BufferSize];

                int bytesSize = 0;
                CachedSize = 0;
                int receivedBufferCount = 0;

                // Download file bytes until the download is paused or completed
                while (true)
                {
                    if (SpeedLimitChanged)
                    {
                        if (Settings.Default.EnableSpeedLimit)
                        {
                            maxBytesPerSecond = (Settings.Default.SpeedLimit * 1024) / DownloadManager.Instance.ActiveDownloads;
                        }
                        else
                        {
                            maxBytesPerSecond = ThrottledStream.Infinite;
                        }
                        throttledStream.MaximumBytesPerSecond = maxBytesPerSecond;
                        SpeedLimitChanged = false;
                    }

                    // Read data from the response stream and write it to the buffer
                    bytesSize = throttledStream.Read(downloadBuffer, 0, downloadBuffer.Length);

                    // If the cache is full or the download is paused or completed, write data from the cache to the temporary file
                    if (Status != DownloadStatus.Downloading || bytesSize == 0 || MaxCacheSize < CachedSize + bytesSize)
                    {
                        // Write data from the cache to the temporary file
                        WriteCacheToFile(downloadCache, CachedSize);

                        DownloadedSize += CachedSize;

                        // Reset the cache
                        downloadCache.Seek(0, SeekOrigin.Begin);
                        CachedSize = 0;

                        // Stop downloading the file if the download is paused or completed
                        if (Status != DownloadStatus.Downloading || bytesSize == 0)
                        {
                            break;
                        }
                    }

                    // Write data from the buffer to the cache
                    downloadCache.Write(downloadBuffer, 0, bytesSize);
                    CachedSize += bytesSize;

                    receivedBufferCount++;
                    if (receivedBufferCount == BufferCountPerNotification)
                    {
                        RaiseDownloadProgressChanged();
                        receivedBufferCount = 0;
                    }
                }

                // Update elapsed time when the download is paused or completed
                ElapsedTime = ElapsedTime.Add(DateTime.UtcNow - lastStartTime);

                // Change status
                if (Status != DownloadStatus.Deleting)
                {
                    if (Status == DownloadStatus.Pausing)
                    {
                        Status = DownloadStatus.Paused;
                        UpdateDownloadDisplay();
                    }
                    else if (Status == DownloadStatus.Queued)
                    {
                        UpdateDownloadDisplay();
                    }
                    else
                    {
                        CompletedOn = DateTime.UtcNow;
                        RaiseDownloadCompleted();
                    }
                }
            }
            catch (Exception ex)
            {
                // Show error in the status
                StatusString = $"Error: {ex.Message}";
                HasError = true;
                RaiseDownloadCompleted();
            }
            finally
            {
                // Close the response stream and cache, stop the thread
                responseStream?.Close();
                throttledStream?.Close();
                webResponse?.Close();
                downloadCache?.Close();
                DownloadThread?.Abort();
            }
        }

        HttpWebRequest CreateWebRequest(string httpMethod, int? timeOut = null)
        {
            if (string.IsNullOrWhiteSpace(nameof(httpMethod)))
                throw new ArgumentNullException(nameof(httpMethod));

            var webRequest = (HttpWebRequest)WebRequest.Create(Url);
            webRequest.Method = httpMethod;
            if (timeOut.HasValue)
            {
                webRequest.Timeout = timeOut.Value;
            }

            if (ServerLogin != null)
            {
                webRequest.PreAuthenticate = true;
                webRequest.Credentials = ServerLogin;
            }
            else
            {
                webRequest.Credentials = CredentialCache.DefaultCredentials;
            }

            if (Settings.Default.ManualProxyConfig && !string.IsNullOrEmpty(Settings.Default.HttpProxy))
            {
                Proxy = new WebProxy
                {
                    Address = new Uri($"http://{Settings.Default.HttpProxy}:{Settings.Default.ProxyPort}"),
                    BypassProxyOnLocal = false
                };
                if (!string.IsNullOrEmpty(Settings.Default.ProxyUsername) && Settings.Default.ProxyPassword != null)
                {
                    Proxy.Credentials = new NetworkCredential(Settings.Default.ProxyUsername, Settings.Default.ProxyPassword);
                }
            }

            webRequest.Proxy = Proxy ?? WebRequest.DefaultWebProxy;

            return webRequest;
        }

        #endregion
    }
}