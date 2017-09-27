using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace SGet
{
    public class DownloadManager
    {
        public static DownloadManager Instance { get; } = new DownloadManager();

        static NumberFormatInfo numberFormat = NumberFormatInfo.InvariantInfo;

        // Collection which contains all download clients, bound to the DataGrid control
        public ObservableCollection<WebDownloadClient> DownloadsList = new ObservableCollection<WebDownloadClient>();

        #region Properties

        // Number of currently active downloads
        public int ActiveDownloads
        {
            get
            {
                var active = DownloadsList
                    .OfType<WebDownloadClient>()
                    .Where(d => !d.HasError && (d.Status == DownloadStatus.Waiting || d.Status == DownloadStatus.Downloading))
                    .ToList()
                    .Count;
                return active;
            }
        }

        // Number of completed downloads
        public int CompletedDownloads
        {
            get
            {
                var completed = DownloadsList
                    .OfType<WebDownloadClient>()
                    .Where(d => d.Status == DownloadStatus.Completed)
                    .ToList()
                    .Count;
                return completed;
            }
        }

        // Total number of downloads in the list
        public int TotalDownloads
        {
            get
            {
                return DownloadsList.Count;
            }
        }

        #endregion

        #region Methods

        // Format file size or downloaded size string
        public static string FormatSizeString(long byteSize)
        {
            double kiloByteSize = byteSize / 1024D;
            double megaByteSize = kiloByteSize / 1024D;
            double gigaByteSize = megaByteSize / 1024D;

            if (byteSize < 1024)
                return String.Format(numberFormat, "{0} B", byteSize);
            if (byteSize < 1048576)
                return String.Format(numberFormat, "{0:0.00} kB", kiloByteSize);
            if (byteSize < 1073741824)
                return String.Format(numberFormat, "{0:0.00} MB", megaByteSize);
            return String.Format(numberFormat, "{0:0.00} GB", gigaByteSize);
        }

        // Format download speed string
        public static string FormatSpeedString(int speed)
        {
            float kbSpeed = speed / 1024F;
            float mbSpeed = kbSpeed / 1024F;

            if (speed <= 0)
                return String.Empty;
            if (speed < 1024)
                return speed.ToString() + " B/s";
            if (speed < 1048576)
                return kbSpeed.ToString("#.00", numberFormat) + " kB/s";
            return mbSpeed.ToString("#.00", numberFormat) + " MB/s";
        }

        // Format time span string so it can display values of more than 24 hours
        public static string FormatTimeSpanString(TimeSpan span)
        {
            string hours = ((int)span.TotalHours).ToString();
            string minutes = span.Minutes.ToString();
            string seconds = span.Seconds.ToString();
            if ((int)span.TotalHours < 10)
                hours = "0" + hours;
            if (span.Minutes < 10)
                minutes = "0" + minutes;
            if (span.Seconds < 10)
                seconds = "0" + seconds;

            return String.Format("{0}:{1}:{2}", hours, minutes, seconds);
        }

        #endregion
    }
}
