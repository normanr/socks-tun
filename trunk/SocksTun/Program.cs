using System;
using System.Collections.Generic;
using System.Configuration.Install;
using System.Linq;
using System.Reflection;
using System.ServiceProcess;
using System.Text;

namespace SocksTun
{
	static class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		static void Main(string[] args)
		{
			if (args.Length > 0)
			{
				switch (args[0].ToLower())
				{
					case "--foreground":
					case "/foreground":
					case "-f":
					case "/f":
						(new SocksTunService()).Run(args);
						return;
					case "--install":
					case "/install":
					case "-i":
					case "/i":
						ManagedInstallerClass.InstallHelper(new[] {Assembly.GetEntryAssembly().Location});
						return;
					case "--uninstall":
					case "/uninstall":
					case "-u":
					case "/u":
						ManagedInstallerClass.InstallHelper(new[] { "/uninstall", Assembly.GetEntryAssembly().Location });
						return;
				}
			}
			ServiceBase[] ServicesToRun;
			ServicesToRun = new ServiceBase[] 
			{ 
				new SocksTunService() 
			};
			ServiceBase.Run(ServicesToRun);
		}
	}
}
