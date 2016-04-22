using System;
using System.IO;
using System.Windows.Forms;

namespace Dispatcher
{
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Form1(args));
            }
            catch(Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
    }
}