using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using Windows.Devices.Enumeration;
using Microsoft.Maker.RemoteWiring;
using Microsoft.Maker.Serial;
using Waher.Events;
using Waher.Persistence;
using Waher.Persistence.Files;
using Waher.Persistence.Filters;
using Waher.Persistence.Serialization;
using Waher.Runtime.Inventory;

using Sensor.History;

namespace Sensor
{
	/// <summary>
	/// Provides application-specific behavior to supplement the default Application class.
	/// </summary>
	sealed partial class App : Application
	{
		private FilesProvider db = null;
		private UsbSerial arduinoUsb = null;
		private RemoteDevice arduino = null;
		private Timer sampleTimer = null;

		private const int windowSize = 10;
		private const int spikePos = windowSize / 2;
		private int?[] windowA0 = new int?[windowSize];
		private int nrA0 = 0;
		private int sumA0 = 0;

		private int? lastMinute = null;
		private double? minLight = null;
		private double? maxLight = null;
		private double sumLight = 0;
		private int sumMotion = 0;
		private int nrTerms = 0;
		private DateTime minLightAt = DateTime.MinValue;
		private DateTime maxLightAt = DateTime.MinValue;

		/// <summary>
		/// Initializes the singleton application object.  This is the first line of authored code
		/// executed, and as such is the logical equivalent of main() or WinMain().
		/// </summary>
		public App()
		{
			this.InitializeComponent();
			this.Suspending += OnSuspending;
		}

		/// <summary>
		/// Invoked when the application is launched normally by the end user.  Other entry points
		/// will be used such as when the application is launched to open a specific file.
		/// </summary>
		/// <param name="e">Details about the launch request and process.</param>
		protected override void OnLaunched(LaunchActivatedEventArgs e)
		{
			// Do not repeat app initialization when the Window already has content,
			// just ensure that the window is active
			if (!(Window.Current.Content is Frame rootFrame))
			{
				// Create a Frame to act as the navigation context and navigate to the first page
				rootFrame = new Frame();

				rootFrame.NavigationFailed += OnNavigationFailed;

				if (e.PreviousExecutionState == ApplicationExecutionState.Terminated)
				{
					//TODO: Load state from previously suspended application
				}

				// Place the frame in the current Window
				Window.Current.Content = rootFrame;
			}

			if (e.PrelaunchActivated == false)
			{
				if (rootFrame.Content is null)
				{
					// When the navigation stack isn't restored navigate to the first page,
					// configuring the new page by passing required information as a navigation
					// parameter
					rootFrame.Navigate(typeof(MainPage), e.Arguments);
				}
				// Ensure the current window is active
				Window.Current.Activate();
				Task.Run((Action)this.Init);
			}
		}

		private async void Init()
		{
			try
			{
				Log.Informational("Starting application.");
				Types.Initialize(
					typeof(FilesProvider).GetTypeInfo().Assembly,
					typeof(ObjectSerializer).GetTypeInfo().Assembly,    // Waher.Persistence.Serialization was broken out of Waher.Persistence.FilesLW after the publishing of the MIoT book.
					typeof(App).GetTypeInfo().Assembly);

				db = new FilesProvider(Windows.Storage.ApplicationData.Current.LocalFolder.Path +
					Path.DirectorySeparatorChar + "Data", "Default", 8192, 1000, 8192, Encoding.UTF8, 10000);
				Database.Register(db);
				await db.RepairIfInproperShutdown(null);
				await db.Start();

				DeviceInformationCollection Devices = await UsbSerial.listAvailableDevicesAsync();
				DeviceInformation DeviceInfo = this.FindDevice(Devices, "Arduino", "USB Serial Device");
				if (DeviceInfo is null)
					Log.Error("Unable to find Arduino device.");
				else
				{
					Log.Informational("Connecting to " + DeviceInfo.Name);

					this.arduinoUsb = new UsbSerial(DeviceInfo);
					this.arduinoUsb.ConnectionEstablished += () =>
						Log.Informational("USB connection established.");

					this.arduino = new RemoteDevice(this.arduinoUsb);
					this.arduino.DeviceReady += () =>
					{
						Log.Informational("Device ready.");

						this.arduino.pinMode(13, PinMode.OUTPUT);    // Onboard LED.
						this.arduino.digitalWrite(13, PinState.HIGH);

						this.arduino.pinMode(8, PinMode.INPUT);      // PIR sensor (motion detection).
						MainPage.Instance.DigitalPinUpdated(8, this.arduino.digitalRead(8));

						this.arduino.pinMode(9, PinMode.OUTPUT);     // Relay.
						this.arduino.digitalWrite(9, 0);             // Relay set to 0

						this.arduino.pinMode("A0", PinMode.ANALOG); // Light sensor.
						MainPage.Instance.AnalogPinUpdated("A0", this.arduino.analogRead("A0"));

						this.sampleTimer = new Timer(this.SampleValues, null, 1000 - DateTime.Now.Millisecond, 1000);
					};

					this.arduino.AnalogPinUpdated += (pin, value) =>
					{
						MainPage.Instance.AnalogPinUpdated(pin, value);
					};

					this.arduino.DigitalPinUpdated += (pin, value) =>
					{
						MainPage.Instance.DigitalPinUpdated(pin, value);
					};

					this.arduinoUsb.ConnectionFailed += message =>
					{
						Log.Error("USB connection failed: " + message);
					};

					this.arduinoUsb.ConnectionLost += message =>
					{
						Log.Error("USB connection lost: " + message);
					};

					this.arduinoUsb.begin(57600, SerialConfig.SERIAL_8N1);
				}
			}
			catch (Exception ex)
			{
				Log.Emergency(ex);

				MessageDialog Dialog = new MessageDialog(ex.Message, "Error");
				await MainPage.Instance.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
					async () => await Dialog.ShowAsync());
			}
		}

		private DeviceInformation FindDevice(DeviceInformationCollection Devices, params string[] DeviceNames)
		{
			foreach (string DeviceName in DeviceNames)
			{
				foreach (DeviceInformation DeviceInfo in Devices)
				{
					if (DeviceInfo.IsEnabled && DeviceInfo.Name.StartsWith(DeviceName))
						return DeviceInfo;
				}
			}

			return null;
		}

		private async void SampleValues(object State)
		{
			try
			{
				ushort A0 = this.arduino.analogRead("A0");
				PinState D8 = this.arduino.digitalRead(8);

				if (this.windowA0[0].HasValue)
				{
					this.sumA0 -= this.windowA0[0].Value;
					this.nrA0--;
				}

				Array.Copy(this.windowA0, 1, this.windowA0, 0, windowSize - 1);
				this.windowA0[windowSize - 1] = A0;
				this.sumA0 += A0;
				this.nrA0++;

				double AvgA0 = ((double)this.sumA0) / this.nrA0;
				int? v;

				if (this.nrA0 >= windowSize - 2)
				{
					int NrLt = 0;
					int NrGt = 0;

					foreach (int? Value in this.windowA0)
					{
						if (Value.HasValue)
						{
							if (Value.Value < AvgA0)
								NrLt++;
							else if (Value.Value > AvgA0)
								NrGt++;
						}
					}

					if (NrLt == 1 || NrGt == 1)
					{
						v = this.windowA0[spikePos];

						if (v.HasValue)
						{
							if ((NrLt == 1 && v.Value < AvgA0) || (NrGt == 1 && v.Value > AvgA0))
							{
								this.sumA0 -= v.Value;
								this.nrA0--;
								this.windowA0[spikePos] = null;

								AvgA0 = ((double)this.sumA0) / this.nrA0;

								Log.Informational("Spike removed.", new KeyValuePair<string, object>("A0", v.Value));
							}
						}
					}
				}

				int i, n;

				for (AvgA0 = i = n = 0; i < spikePos; i++)
				{
					if ((v = this.windowA0[i]).HasValue)
					{
						n++;
						AvgA0 += v.Value;
					}
				}

				if (n > 0)
				{
					AvgA0 /= n;
					double Light = (100.0 * AvgA0) / 1024;
					MainPage.Instance.LightUpdated(Light, 2, "%");

					this.sumLight += Light;
					this.sumMotion += (D8 == PinState.HIGH ? 1 : 0);
					this.nrTerms++;

					DateTime Timestamp = DateTime.Now;

					if (!this.minLight.HasValue || Light < this.minLight.Value)
					{
						this.minLight = Light;
						this.minLightAt = Timestamp;
					}

					if (!this.maxLight.HasValue || Light > this.maxLight.Value)
					{
						this.maxLight = Light;
						this.maxLightAt = Timestamp;
					}

					if (!this.lastMinute.HasValue)
						this.lastMinute = Timestamp.Minute;
					else if (this.lastMinute.Value != Timestamp.Minute)
					{
						this.lastMinute = Timestamp.Minute;

						LastMinute Rec = new LastMinute()
						{
							Timestamp = Timestamp,
							Light = Light,
							Motion = D8,
							MinLight = this.minLight,
							MinLightAt = this.minLightAt,
							MaxLight = this.maxLight,
							MaxLightAt = this.maxLightAt,
							AvgLight = (this.nrTerms == 0 ? (double?)null : this.sumLight / this.nrTerms),
							AvgMotion = (this.nrTerms == 0 ? (double?)null : (this.sumMotion * 100.0) / this.nrTerms)
						};

						await Database.Insert(Rec);

						this.minLight = null;
						this.minLightAt = DateTime.MinValue;
						this.maxLight = null;
						this.maxLightAt = DateTime.MinValue;
						this.sumLight = 0;
						this.sumMotion = 0;
						this.nrTerms = 0;

						foreach (LastMinute Rec2 in await Database.Find<LastMinute>(new FilterFieldLesserThan("Timestamp", Timestamp.AddMinutes(-100))))
							await Database.Delete(Rec2);

						if (Timestamp.Minute == 0)
						{
							DateTime From = new DateTime(Timestamp.Year, Timestamp.Month, Timestamp.Day, Timestamp.Hour, 0, 0).AddHours(-1);
							DateTime To = From.AddHours(1);
							int NLight = 0;
							int NMotion = 0;

							LastHour HourRec = new LastHour()
							{
								Timestamp = Timestamp,
								Light = Light,
								Motion = D8,
								MinLight = Rec.MinLight,
								MinLightAt = Rec.MinLightAt,
								MaxLight = Rec.MaxLight,
								MaxLightAt = Rec.MaxLightAt,
								AvgLight = 0,
								AvgMotion = 0
							};

							foreach (LastMinute Rec2 in await Database.Find<LastMinute>(0, 60, new FilterAnd(
								new FilterFieldLesserThan("Timestamp", To),
								new FilterFieldGreaterOrEqualTo("Timestamp", From))))
							{
								if (Rec2.AvgLight.HasValue)
								{
									HourRec.AvgLight += Rec2.AvgLight.Value;
									NLight++;
								}

								if (Rec2.AvgMotion.HasValue)
								{
									HourRec.AvgMotion += Rec2.AvgMotion.Value;
									NMotion++;
								}

								if (Rec2.MinLight < HourRec.MinLight)
								{
									HourRec.MinLight = Rec2.MinLight;
									HourRec.MinLightAt = Rec.MinLightAt;
								}

								if (Rec2.MaxLight < HourRec.MaxLight)
								{
									HourRec.MaxLight = Rec2.MaxLight;
									HourRec.MaxLightAt = Rec.MaxLightAt;
								}
							}

							if (NLight == 0)
								HourRec.AvgLight = null;
							else
								HourRec.AvgLight /= NLight;

							if (NMotion == 0)
								HourRec.AvgMotion = null;
							else
								HourRec.AvgMotion /= NMotion;

							await Database.Insert(HourRec);

							foreach (LastHour Rec2 in await Database.Find<LastHour>(new FilterFieldLesserThan("Timestamp", Timestamp.AddHours(-100))))
								await Database.Delete(Rec2);

							if (Timestamp.Hour == 0)
							{
								From = new DateTime(Timestamp.Year, Timestamp.Month, Timestamp.Day, 0, 0, 0).AddDays(-1);
								To = From.AddDays(1);
								NLight = 0;
								NMotion = 0;

								LastDay DayRec = new LastDay()
								{
									Timestamp = Timestamp,
									Light = Light,
									Motion = D8,
									MinLight = HourRec.MinLight,
									MinLightAt = HourRec.MinLightAt,
									MaxLight = HourRec.MaxLight,
									MaxLightAt = HourRec.MaxLightAt,
									AvgLight = 0,
									AvgMotion = 0
								};

								foreach (LastHour Rec2 in await Database.Find<LastHour>(0, 24, new FilterAnd(
									new FilterFieldLesserThan("Timestamp", To),
									new FilterFieldGreaterOrEqualTo("Timestamp", From))))
								{
									if (Rec2.AvgLight.HasValue)
									{
										DayRec.AvgLight += Rec2.AvgLight.Value;
										NLight++;
									}

									if (Rec2.AvgMotion.HasValue)
									{
										DayRec.AvgMotion += Rec2.AvgMotion.Value;
										NMotion++;
									}

									if (Rec2.MinLight < DayRec.MinLight)
									{
										DayRec.MinLight = Rec2.MinLight;
										DayRec.MinLightAt = Rec.MinLightAt;
									}

									if (Rec2.MaxLight < DayRec.MaxLight)
									{
										DayRec.MaxLight = Rec2.MaxLight;
										DayRec.MaxLightAt = Rec.MaxLightAt;
									}
								}

								if (NLight == 0)
									DayRec.AvgLight = null;
								else
									DayRec.AvgLight /= NLight;

								if (NMotion == 0)
									DayRec.AvgMotion = null;
								else
									DayRec.AvgMotion /= NMotion;

								await Database.Insert(DayRec);

								foreach (LastDay Rec2 in await Database.Find<LastDay>(new FilterFieldLesserThan("Timestamp", Timestamp.AddDays(-100))))
									await Database.Delete(Rec2);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Log.Critical(ex);
			}
		}

		/// <summary>
		/// Invoked when Navigation to a certain page fails
		/// </summary>
		/// <param name="sender">The Frame which failed navigation</param>
		/// <param name="e">Details about the navigation failure</param>
		void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
		{
			throw new Exception("Failed to load Page " + e.SourcePageType.FullName);
		}

		/// <summary>
		/// Invoked when application execution is being suspended.  Application state is saved
		/// without knowing whether the application will be terminated or resumed with the contents
		/// of memory still intact.
		/// </summary>
		/// <param name="sender">The source of the suspend request.</param>
		/// <param name="e">Details about the suspend request.</param>
		private void OnSuspending(object sender, SuspendingEventArgs e)
		{
			var deferral = e.SuspendingOperation.GetDeferral();

			this.sampleTimer?.Dispose();
			this.sampleTimer = null;

			if (this.arduino != null)
			{
				this.arduino.digitalWrite(13, PinState.LOW);
				this.arduino.pinMode(13, PinMode.INPUT);     // Onboard LED.
				this.arduino.pinMode(9, PinMode.INPUT);      // Relay.

				this.arduino.Dispose();
				this.arduino = null;
			}

			if (this.arduinoUsb != null)
			{
				this.arduinoUsb.end();
				this.arduinoUsb.Dispose();
				this.arduinoUsb = null;
			}

			db?.Stop()?.Wait();
			db?.Flush()?.Wait();

			Log.Terminate();

			deferral.Complete();
		}
	}
}
