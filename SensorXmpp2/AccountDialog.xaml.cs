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

namespace SensorXmpp
{
	public sealed partial class AccountDialog : ContentDialog
	{
		private string host = string.Empty;
		private int port = 5222;
		private string userName = string.Empty;
		private string password = string.Empty;

		public AccountDialog(string Host, int Port, string UserName)
		{
			this.host = Host;
			this.port = Port;
			this.userName = UserName;

			this.InitializeComponent();

			this.HostInput.Text = Host;
			this.PortInput.Text = Port.ToString();
			this.UserNameInput.Text = UserName;
		}

		public string Host
		{
			get { return this.host; }
			set { this.host = value; }
		}

		public int Port
		{
			get { return this.port; }
			set { this.port = value; }
		}

		public string UserName
		{
			get { return this.userName; }
			set { this.userName = value; }
		}

		public string Password
		{
			get { return this.password; }
			set { this.password = value; }
		}

		private void ContentDialog_ConnectButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
		{
			if (ushort.TryParse(this.PortInput.Text, out ushort Port))
				this.port = Port;
			else
				args.Cancel = true;

			this.host = this.HostInput.Text;
			this.userName = this.UserNameInput.Text;
			this.password = this.PasswordInput.Password;
		}

		private void ContentDialog_CancelButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
		{
		}
	}
}
