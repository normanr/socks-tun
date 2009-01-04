namespace SocksTun
{
	partial class SocksTunService
	{
		/// <summary> 
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Component Designer generated code

		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.components = new System.ComponentModel.Container();
			this.backgroundWorker1 = new System.ComponentModel.BackgroundWorker();
			this.mappingCleanupTimer = new System.Windows.Forms.Timer(this.components);
			// 
			// backgroundWorker1
			// 
			this.backgroundWorker1.WorkerSupportsCancellation = true;
			this.backgroundWorker1.DoWork += new System.ComponentModel.DoWorkEventHandler(this.backgroundWorker1_DoWork);
			this.backgroundWorker1.RunWorkerCompleted += new System.ComponentModel.RunWorkerCompletedEventHandler(this.backgroundWorker1_RunWorkerCompleted);
			// 
			// mappingCleanupTimer
			// 
			this.mappingCleanupTimer.Interval = 10000;
			this.mappingCleanupTimer.Tick += new System.EventHandler(this.mappingCleanupTimer_Tick);
			// 
			// SocksTunService
			// 
			this.ServiceName = "Service1";

		}

		#endregion

		private System.ComponentModel.BackgroundWorker backgroundWorker1;
		private System.Windows.Forms.Timer mappingCleanupTimer;
	}
}
