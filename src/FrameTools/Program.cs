using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("FrameTools")]
[assembly: AssemblyDescription("DL/T 645 与南网本地、上行通信报文工具")]
[assembly: AssemblyVersion(FrameTools.Program.Version + ".0")]
[assembly: AssemblyFileVersion(FrameTools.Program.Version + ".0")]

namespace FrameTools
{
    internal static class Program
    {
        internal const string Version = "0.6.0";

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
