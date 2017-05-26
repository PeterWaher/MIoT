using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
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
using Microsoft.Maker.RemoteWiring;
using Waher.Events;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Sensor
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

			this.events = new MainPage.Events();
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

		public async void AnalogPinUpdated(string Pin, ushort Value)
		{
			if (Pin == "A0")
				await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => this.A0.Text = Value.ToString());
		}

		public async void DigitalPinUpdated(byte Pin, PinState Value)
		{
			if (Pin == 0)
				await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => this.D0.Text = Value.ToString());
		}

		public async void LightUpdated(double Value, int NrDec, string Unit)
		{
			string s = Value.ToString("F" + NrDec.ToString()) + " " + Unit;
			await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => this.Light.Text = s);
		}

		public async void AddLogMessage(string Message)
		{
			await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
			{
				this.EventsPanel.Children.Insert(0, new TextBlock() { Text = Message });

				while (this.EventsPanel.Children.Count > 1000)
					this.EventsPanel.Children.RemoveAt(1000);
			});
		}

		private class Events : EventSink
		{
			public Events() : base(string.Empty)
			{
			}

			public override void Queue(Event Event)
			{
				MainPage.instance.AddLogMessage(Event.Message);
			}
		}

	}
}
