using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
		private StreamWriter d8Output = null;
		private StreamWriter a0Output = null;
		private StreamWriter lightOutput = null;
		private StreamWriter eventOutput = null;
		private DateTime prevD8TP = DateTime.MinValue;
		private DateTime prevA0TP = DateTime.MinValue;
		private DateTime prevLightTP = DateTime.MinValue;
		private bool d8First;
		private bool a0First;
		private bool lightFirst;
		private bool eventFirst;
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

			this.CloseFiles();
		}

		public static MainPage Instance
		{
			get { return instance; }
		}

		public async void AnalogPinUpdated(string Pin, ushort Value)
		{
			if (Pin == "A0")
			{
				DateTime TP = DateTime.Now;
				StreamWriter w;

				if ((w = this.a0Output) != null)
				{
					lock (w)
					{
						if (!this.WriteNewRecord(w, TP, ref this.prevA0TP, ref this.a0First))
							return;

						w.Write(Value.ToString());
						w.Write(']');
					}
				}

				await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => this.A0.Text = Value.ToString());
			}
		}

		public async void DigitalPinUpdated(byte Pin, PinState Value)
		{
			if (Pin == 8)
			{
				DateTime TP = DateTime.Now;
				StreamWriter w;

				if ((w = this.d8Output) != null)
				{
					lock (w)
					{
						if (!this.WriteNewRecord(w, TP, ref this.prevD8TP, ref this.d8First))
							return;

						w.Write("\"");
						w.Write(Value.ToString());
						w.Write("\"]");
					}
				}

				await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => this.D8.Text = Value.ToString());
			}
		}

		public async void LightUpdated(double Value, int NrDec, string Unit)
		{
			DateTime TP = DateTime.Now;
			StreamWriter w;
			string ValueStr = Value.ToString("F" + NrDec.ToString());

			if ((w = this.lightOutput) != null)
			{
				lock (w)
				{
					if (!this.WriteNewRecord(w, TP, ref this.prevLightTP, ref this.lightFirst))
						return;

					w.Write(ValueStr.Replace(System.Globalization.NumberFormatInfo.CurrentInfo.NumberDecimalSeparator, "."));
					w.Write(",\"");
					w.Write(Unit);
					w.Write("\"]");
				}
			}

			await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => this.Light.Text = ValueStr + " " + Unit);
		}

		public async void AddLogMessage(string Message)
		{
			DateTime TP = DateTime.Now;
			StreamWriter w;

			await this.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
			{
				this.EventsPanel.Children.Insert(0, new TextBlock() { Text = Message, TextWrapping = TextWrapping.Wrap });

				while (this.EventsPanel.Children.Count > 1000)
					this.EventsPanel.Children.RemoveAt(1000);
			});

			if ((w = this.eventOutput) != null)
			{
				lock (w)
				{
					DateTime PrevLog = DateTime.MinValue;	// Makes sure all events are logged.

					if (!this.WriteNewRecord(w, TP, ref PrevLog, ref this.eventFirst))
						return;

					w.Write("\"");
					w.Write(Message);
					w.Write("\"]");
				}
			}
		}

		private class Events : EventSink
		{
			public Events() : base(string.Empty)
			{
			}

			public override void Queue(Event Event)
			{
				StringBuilder sb = new StringBuilder(Event.Message);

				if (!string.IsNullOrEmpty(Event.Object))
				{
					sb.Append(' ');
					sb.Append(Event.Object);
				}

				if (!string.IsNullOrEmpty(Event.Actor))
				{
					sb.Append(' ');
					sb.Append(Event.Actor);
				}

				foreach (KeyValuePair<string, object> Parameter in Event.Tags)
				{
					sb.Append(" [");
					sb.Append(Parameter.Key);
					sb.Append("=");
					if (Parameter.Value != null)
						sb.Append(Parameter.Value.ToString());
					sb.Append("]");
				}

				if (Event.Type >= EventType.Critical && !string.IsNullOrEmpty(Event.StackTrace))
				{
					sb.Append("\r\n\r\n");
					sb.Append(Event.StackTrace);
				}

				MainPage.Instance.AddLogMessage(sb.ToString());
			}
		}

		private bool WriteNewRecord(StreamWriter w, DateTime TP, ref DateTime Prev, ref bool First)
		{
			if (First)
			{
				First = false;
				w.Write("[[");
			}
			else
			{
				if (TP.Year == Prev.Year &&
					TP.Month == Prev.Month &&
					TP.Day == Prev.Day &&
					TP.Hour == Prev.Hour &&
					TP.Minute == Prev.Minute &&
					TP.Second == Prev.Second &&
					TP.Millisecond == Prev.Millisecond)
				{
					return false;
				}

				w.WriteLine(",");
				w.Write(" [");
			}

			Prev = TP;

			w.Write("DateTime(");
			w.Write(TP.Year.ToString("D4"));
			w.Write(",");
			w.Write(TP.Month.ToString("D2"));
			w.Write(",");
			w.Write(TP.Day.ToString("D2"));
			w.Write(",");
			w.Write(TP.Hour.ToString("D2"));
			w.Write(",");
			w.Write(TP.Minute.ToString("D2"));
			w.Write(",");
			w.Write(TP.Second.ToString("D2"));
			w.Write(",");
			w.Write(TP.Millisecond.ToString("D3"));
			w.Write("),");

			return true;
		}

		private void OutputToFile_Click(object sender, RoutedEventArgs e)
		{
			this.CloseFiles();

			if (this.OutputToFile.IsChecked.HasValue && this.OutputToFile.IsChecked.Value)
			{
				this.d8First = true;
				this.d8Output = File.CreateText(Windows.Storage.ApplicationData.Current.LocalFolder.Path +
					Path.DirectorySeparatorChar + "D8.script");

				this.a0First = true;
				this.a0Output = File.CreateText(Windows.Storage.ApplicationData.Current.LocalFolder.Path +
					Path.DirectorySeparatorChar + "A0.script");

				this.lightFirst = true;
				this.lightOutput = File.CreateText(Windows.Storage.ApplicationData.Current.LocalFolder.Path +
					Path.DirectorySeparatorChar + "Light.script");

				this.eventFirst = true;
				this.eventOutput = File.CreateText(Windows.Storage.ApplicationData.Current.LocalFolder.Path +
					Path.DirectorySeparatorChar + "Events.script");
			}
		}

		private void CloseFiles()
		{
			this.CloseFile(ref this.d8Output);
			this.CloseFile(ref this.a0Output);
			this.CloseFile(ref this.lightOutput);
			this.CloseFile(ref this.eventOutput);
		}

		private void CloseFile(ref StreamWriter File)
		{
			StreamWriter w;

			if ((w = File) != null)
			{
				File = null;

				lock (w)
				{
					w.WriteLine("];");
					w.Flush();
					w.Dispose();
				}
			}
		}

	}
}
