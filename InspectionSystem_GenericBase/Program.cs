using System;
using System.Windows.Forms;

namespace InspectionSystem_GenericBase
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // プラットフォームエラー回避
            AppContext.SetSwitch("System.Drawing.EnableUnixSupport", true);

            ApplicationConfiguration.Initialize();
            Application.Run(new Form1());
        }
    }
}
