using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SGet
{
    public interface IDialog
    {
        bool? ShowDialog();
    }

    public interface IMainWindow : IDialog
    {
        void PropertyChangedHandler(object sender, PropertyChangedEventArgs e);
        void StatusChangedHandler(object sender, EventArgs e);
        void DownloadCompletedHandler(object sender, EventArgs e);
        void Close();
    }
}
