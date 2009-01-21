using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using SocksTun.Services;

namespace SocksTun
{
	public partial class SocksTunService : ServiceBase
	{
		public SocksTunService()
		{
			InitializeComponent();
			debug.LogLevel = 2;
		}

		public void Run(string[] args)
		{
			Console.CancelKeyPress += Console_CancelKeyPress;
			debug.Writer = Console.Out;
			OnStart(args);
			debug.Log(-1, "SocksTun running in foreground mode, press enter to exit");
			Console.ReadLine();
			debug.Log(-1, "Shutting down...");
			OnStop();
		}

		static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
		{
			e.Cancel = true;
		}

		private readonly DebugWriter debug = new DebugWriter();
		private readonly IDictionary<string, IService> services = new Dictionary<string, IService>();

		protected override void OnStart(string[] args)
		{
			services["connectionTracker"] = new ConnectionTracker(debug, services);
			services["natter"] = new Natter(debug, services);
			services["logServer"] = new LogServer(debug, services);
			services["transparentSocksServer"] = new TransparentSocksServer(debug, services);

			services["connectionTracker"].Start();
			services["natter"].Start();
			services["logServer"].Start();
			services["transparentSocksServer"].Start();
		}

		protected override void OnStop()
		{
			services["transparentSocksServer"].Stop();
			services["logServer"].Stop();
			services["natter"].Stop();
			services["connectionTracker"].Stop();
		}
	}
}
