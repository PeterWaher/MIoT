//#define GPIO

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Devices.Gpio;
using Windows.Foundation;
using Windows.Foundation.Collections;
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
using Waher.Runtime.Settings;
using Waher.Script;

namespace Actuator
{
	/// <summary>
	/// Provides application-specific behavior to supplement the default Application class.
	/// </summary>
	sealed partial class App : Application
	{
		private static App instance = null;

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
			Frame rootFrame = Window.Current.Content as Frame;

			// Do not repeat app initialization when the Window already has content,
			// just ensure that the window is active
			if (rootFrame == null)
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
				if (rootFrame.Content == null)
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
				Log.Informational("Starting application.");

				Types.Initialize(
					typeof(FilesProvider).GetTypeInfo().Assembly,
					typeof(App).GetTypeInfo().Assembly,
					typeof(RuntimeSettings).GetTypeInfo().Assembly);

				Database.Register(new FilesProvider(Windows.Storage.ApplicationData.Current.LocalFolder.Path +
					Path.DirectorySeparatorChar + "Data", "Default", 8192, 1000, 8192, Encoding.UTF8, 10000));

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
				foreach (DeviceInformation DeviceInfo in Devices)
				{
					if (DeviceInfo.IsEnabled && DeviceInfo.Name.StartsWith("Arduino"))
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

								this.arduino.pinMode(0, PinMode.INPUT);      // PIR sensor (motion detection).

								this.arduino.pinMode(1, PinMode.OUTPUT);     // Relay.

								bool LastOn = await RuntimeSettings.GetAsync("Actuator.Output", false);
								this.arduino.digitalWrite(1, LastOn ? PinState.HIGH : PinState.LOW);

								await MainPage.Instance.OutputSet(LastOn);

								Log.Informational("Setting Control Parameter.", string.Empty, "Startup",
									new KeyValuePair<string, object>("Output", LastOn));

								this.arduino.pinMode("A0", PinMode.ANALOG); // Light sensor.
							}
							catch (Exception ex)
							{
								Log.Critical(ex);
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
						break;
					}
				}
#endif
			}
			catch (Exception ex)
			{
				Log.Emergency(ex);

				MessageDialog Dialog = new MessageDialog(ex.Message, "Error");
				await Dialog.ShowAsync();
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
				this.arduino.digitalWrite(1, On ? PinState.HIGH : PinState.LOW);
#endif
				await RuntimeSettings.SetAsync("Actuator.Output", On);

				Log.Informational("Setting Control Parameter.", string.Empty, Actor ?? "Windows user",
					new KeyValuePair<string, object>("Output", On));

				if (Actor != null)
					await MainPage.Instance.OutputSet(On);
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

#if GPIO
			if (this.gpioPin != null)
			{
				this.gpioPin.Dispose();
				this.gpioPin = null;
			}
#else
			if (this.arduino != null)
			{
				this.arduino.digitalWrite(13, PinState.LOW);
				this.arduino.pinMode(13, PinMode.INPUT);     // Onboard LED.
				this.arduino.pinMode(1, PinMode.INPUT);      // Relay.

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
			Log.Terminate();

			deferral.Complete();
		}
	}
}
