using System;
using System.Reflection;
using System.Windows.Forms;

[assembly: AssemblyTitle("FrameTools")]
[assembly: AssemblyDescription("DL/T 645 与南网本地、上行通信报文工具")]
[assembly: AssemblyVersion("0.2.1.0")]
[assembly: AssemblyFileVersion("0.2.1.0")]

namespace FrameTools
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
