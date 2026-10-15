using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TRProtocol
{
	public static class Program
	{
		[DllImport("kernel32.dll")]
		private static extern bool AttachConsole(int dwProcessId);

		[STAThread]
		public static int Main(string[] args)
		{
			if (args != null && args.Length > 0 && args[0].Length > 0 && args[0] != "/?")
			{
				AttachConsole(-1);
				try
				{
					DumpResult result = OpcodeDump.DumpFile(args[0].Trim().Trim('"'));
					Console.WriteLine(result.CsvPath);
					Console.WriteLine(result.Status);
					return 0;
				}
				catch (Exception ex)
				{
					Console.Error.WriteLine(ex.Message);
					return 1;
				}
			}

			Application.SetHighDpiMode(HighDpiMode.DpiUnaware);
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new MainForm());
			return 0;
		}
	}
}
