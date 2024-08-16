//#define GPIO

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
#if GPIO
using Windows.Devices.Gpio;
#endif
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

using Windows.Devices.Enumeration;
#if !GPIO
using Microsoft.Maker.RemoteWiring;
using Microsoft.Maker.Serial;
#endif
using Waher.Events;
using Waher.Networking.MQTT;
using Waher.Persistence;
using Waher.Persistence.Files;
using Waher.Persistence.Serialization;
using Waher.Runtime.Settings;
using Waher.Runtime.Inventory;

namespace ActuatorMqtt
{
	/// <summary>
	/// Provides application-specific behavior to supplement the default Application class.
	/// </summary>
	sealed partial class App : Application
	{
		private static App instance = null;
		private FilesProvider db = null;
		private string deviceId;
		private MqttClient mqttClient = null;
		private Timer reconnectionTimer = null;

#if GPIO
		private const int gpioOutputPin = 5;
		private GpioController gpio = null;
		private GpioPin gpioPin = null;
#else
		private UsbSerial arduinoUsb = null;
		private RemoteDevice arduino = null;
#endif

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
				instance = this;
				Window.Current.Activate();
				Task.Run((Action)this.Init);
			}
		}

		private async void Init()
		{
			try
			{
				// Exception types that are logged with an elevated type.
				Log.RegisterAlertExceptionType(true,
					typeof(OutOfMemoryException),
					typeof(StackOverflowException),
					typeof(AccessViolationException),
					typeof(InsufficientMemoryException));

				Log.Informational("Starting application.");

				Types.Initialize(
					typeof(FilesProvider).GetTypeInfo().Assembly,
					typeof(ObjectSerializer).GetTypeInfo().Assembly,    // Waher.Persistence.Serialization was broken out of Waher.Persistence.FilesLW after the publishing of the MIoT book.
					typeof(RuntimeSettings).GetTypeInfo().Assembly,
					typeof(App).GetTypeInfo().Assembly);

				db = await FilesProvider.CreateAsync(Windows.Storage.ApplicationData.Current.LocalFolder.Path +
					Path.DirectorySeparatorChar + "Data", "Default", 8192, 1000, 8192, Encoding.UTF8, 10000);
				Database.Register(db);
				await db.RepairIfInproperShutdown(null);
				await db.Start();

#if GPIO
				gpio = GpioController.GetDefault();
				if (gpio != null)
				{
					if (gpio.TryOpenPin(gpioOutputPin, GpioSharingMode.Exclusive, out this.gpioPin, out GpioOpenStatus Status) &&
						Status == GpioOpenStatus.PinOpened)
					{
						if (this.gpioPin.IsDriveModeSupported(GpioPinDriveMode.Output))
						{
							this.gpioPin.SetDriveMode(GpioPinDriveMode.Output);

							bool LastOn = await RuntimeSettings.GetAsync("Actuator.Output", false);
							this.gpioPin.Write(LastOn ? GpioPinValue.High : GpioPinValue.Low);

							await MainPage.Instance.OutputSet(LastOn);

							Log.Informational("Setting Control Parameter.", string.Empty, "Startup",
								new KeyValuePair<string, object>("Output", LastOn));
						}
						else
							Log.Error("Output mode not supported for GPIO pin " + gpioOutputPin.ToString());
					}
					else
						Log.Error("Unable to get access to GPIO pin " + gpioOutputPin.ToString());
				}
#else
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
					this.arduino.DeviceReady += async () =>
					{
						try
						{
							Log.Informational("Device ready.");

							this.arduino.pinMode(13, PinMode.OUTPUT);    // Onboard LED.
							this.arduino.digitalWrite(13, PinState.HIGH);

							this.arduino.pinMode(8, PinMode.INPUT);      // PIR sensor (motion detection).

							this.arduino.pinMode(9, PinMode.OUTPUT);     // Relay.

							bool LastOn = await RuntimeSettings.GetAsync("Actuator.Output", false);
							this.arduino.digitalWrite(9, LastOn ? PinState.HIGH : PinState.LOW);

							await MainPage.Instance.OutputSet(LastOn);

							Log.Informational("Setting Control Parameter.", string.Empty, "Startup",
								new KeyValuePair<string, object>("Output", LastOn));

							this.arduino.pinMode("A0", PinMode.ANALOG); // Light sensor.
						}
						catch (Exception ex)
						{
							Log.Exception(ex);
						}
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
#endif
				this.deviceId = await RuntimeSettings.GetAsync("DeviceId", string.Empty);
				if (string.IsNullOrEmpty(this.deviceId))
				{
					this.deviceId = Guid.NewGuid().ToString().Replace("-", string.Empty);
					await RuntimeSettings.SetAsync("DeviceId", this.deviceId);
				}

				Log.Informational("Device ID: " + this.deviceId);

				this.mqttClient = new MqttClient("iot.eclipse.org", 8883, true, this.deviceId, string.Empty);
				//this.mqttClient = new MqttClient("iot.eclipse.org", 8883, true, this.deviceId, string.Empty, new LogSniffer());
				this.mqttClient.OnStateChanged += (sender, state) =>
				{
					Log.Informational("MQTT client state changed: " + state.ToString());

					if (state == MqttState.Connected)
						this.mqttClient.SUBSCRIBE("Waher/MIOT/" + this.deviceId + "/Set/+", MqttQualityOfService.AtLeastOnce);

					return Task.CompletedTask;
				};

				this.mqttClient.OnContentReceived += async (sender, e) =>
				{
					try
					{
						if (e.Topic.EndsWith("/On"))
						{
							string s = Encoding.UTF8.GetString(e.Data);
							s = s.Substring(0, 1).ToUpper() + s.Substring(1).ToLower();

							if (bool.TryParse(s, out bool On))
								await this.SetOutput(On, "MQTT");
						}
					}
					catch (Exception ex)
					{
						Log.Exception(ex);
					}
				};

				DateTime Now = DateTime.Now;
				this.reconnectionTimer = new Timer(this.CheckConnection, null, 120000 - Now.Millisecond - Now.Second * 1000, 60000);
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

		private void CheckConnection(object State)
		{
			try
			{
				if (this.mqttClient != null && (this.mqttClient.State == MqttState.Error || this.mqttClient.State == MqttState.Offline))
					this.mqttClient.Reconnect();
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
		}

		internal static App Instance => instance;

		internal async Task SetOutput(bool On, string Actor)
		{
#if GPIO
			if (this.gpioPin != null)
			{
				this.gpioPin.Write(On ? GpioPinValue.High : GpioPinValue.Low);
#else
			if (this.arduino != null)
			{
				this.arduino.digitalWrite(9, On ? PinState.HIGH : PinState.LOW);
#endif
				await RuntimeSettings.SetAsync("Actuator.Output", On);

				Log.Informational("Setting Control Parameter.", string.Empty, Actor ?? "Windows user",
					new KeyValuePair<string, object>("Output", On));

				if (Actor != null)
					await MainPage.Instance.OutputSet(On);

				if (this.mqttClient != null && this.mqttClient.State == MqttState.Connected)
				{
					await this.mqttClient.PUBLISH("Waher/MIOT/" + this.deviceId + "/On", MqttQualityOfService.AtLeastOnce, true,
						Encoding.UTF8.GetBytes(On.ToString()));

					StringBuilder Json = new StringBuilder();

					Json.Append("{\"ts\":\"");
					Json.Append(DateTime.Now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
					Json.Append("\",\"on\":");
					Json.Append(On ? "true" : "false");
					Json.Append('}');

					byte[] Data = Encoding.UTF8.GetBytes(Json.ToString());
					await this.mqttClient.PUBLISH("Waher/MIOT/" + this.deviceId + "/JSON", MqttQualityOfService.AtLeastOnce, true, Data);
				}
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

			if (instance == this)
				instance = null;

			this.mqttClient?.Dispose();
			this.mqttClient = null;

			this.reconnectionTimer?.Dispose();
			this.reconnectionTimer = null;

#if GPIO
			this.gpioPin?.Dispose();
			this.gpioPin = null;
#else
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
#endif
			db?.Stop()?.Wait();
			db?.Flush()?.Wait();

			Log.Terminate();

			deferral.Complete();
		}
	}
}
