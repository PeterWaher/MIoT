using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Content Dialog item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace ControllerXmpp
{
	public sealed partial class AssociationDialog : ContentDialog
	{
		private string jid = string.Empty;
		private string nodeId = string.Empty;
		private string sourceId = string.Empty;
		private string partition = string.Empty;

		public AssociationDialog(string Device)
		{
			this.Title = this.Title.ToString().Replace("%0%", Device);
			this.Block1.Text = this.Block1.Text.Replace("%0%", Device);
			this.Block2.Text = this.Block2.Text.Replace("%0%", Device);

			this.InitializeComponent();
		}

		public string Jid
		{
			get { return this.jid; }
			set { this.jid = value; }
		}

		public string NodeId
		{
			get { return this.nodeId; }
			set { this.nodeId = value; }
		}

		public string SourceId
		{
			get { return this.sourceId; }
			set { this.sourceId = value; }
		}

		public string Partition
		{
			get { return this.partition; }
			set { this.partition = value; }
		}

		private void ContentDialog_ConnectButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
		{
			this.jid = this.JidInput.Text;
			this.nodeId = this.NodeIdInput.Text;
			this.sourceId = this.SourceIdInput.Text;
			this.partition = this.PartitionInput.Text;
		}

		private void ContentDialog_CancelButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
		{
		}
	}
}
