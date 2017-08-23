using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Waher.Events;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace ActuatorCoap
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
		private static MainPage instance = null;
		private Events events;

		public MainPage()
        {
            this.InitializeComponent();

			this.events = new Events();
			Log.Register(this.events);

			if (instance == null)
				instance = this;
		}

		private void Page_Unloaded(object sender, RoutedEventArgs e)
		{
			Log.Unregister(this.events);
			this.events = null;

			if (instance == this)
				instance = null;
		}

		public static MainPage Instance
		{
			get { return instance; }
		}

		public async void AddLogMessage(string Message)
		{
			DateTime TP = DateTime.Now;

			await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
			{
				this.EventsPanel.Children.Insert(0, new TextBlock() { Text = Message, TextWrapping = TextWrapping.Wrap });

				while (this.EventsPanel.Children.Count > 1000)
					this.EventsPanel.Children.RemoveAt(1000);
			});
		}

		private void Relay_Click(object sender, RoutedEventArgs e)
		{
			bool On = this.Relay.IsChecked.HasValue && this.Relay.IsChecked.Value;
			Task.Run(() => App.Instance.SetOutput(On, null));
		}

		internal async Task OutputSet(bool On)
		{
			await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => this.Relay.IsChecked = On);
		}
	}
}
