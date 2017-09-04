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
using Waher.Content;
using Waher.Events;
using Waher.Networking.CoAP;
using Waher.Networking.CoAP.ContentFormats;
using Waher.Networking.CoAP.Options;
using Waher.Persistence;
using Waher.Persistence.Files;
using Waher.Persistence.Filters;
using Waher.Runtime.Settings;
using Waher.Runtime.Inventory;
using Waher.Security;

namespace ActuatorLwm2m
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
		private string deviceId;
		private CoapEndpoint coapEndpoint = null;
		private IUserSource users = new Users();
		private bool? output = null;
		private CoapResource outputResource = null;

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
					typeof(RuntimeSettings).GetTypeInfo().Assembly,
					typeof(IContentEncoder).GetTypeInfo().Assembly,
					typeof(ICoapContentFormat).GetTypeInfo().Assembly,
					typeof(App).GetTypeInfo().Assembly);

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

							this.output = await RuntimeSettings.GetAsync("Actuator.Output", false);
							this.gpioPin.Write(this.output.Value ? GpioPinValue.High : GpioPinValue.Low);

							await MainPage.Instance.OutputSet(this.output.Value);

							Log.Informational("Setting Control Parameter.", string.Empty, "Startup",
								new KeyValuePair<string, object>("Output", this.output.Value));
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

								this.output = await RuntimeSettings.GetAsync("Actuator.Output", false);
								this.arduino.digitalWrite(1, this.output.Value ? PinState.HIGH : PinState.LOW);

								await MainPage.Instance.OutputSet(this.output.Value);

								Log.Informational("Setting Control Parameter.", string.Empty, "Startup",
									new KeyValuePair<string, object>("Output", this.output.Value));

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
				this.deviceId = await RuntimeSettings.GetAsync("DeviceId", string.Empty);
				if (string.IsNullOrEmpty(this.deviceId))
				{
					this.deviceId = Guid.NewGuid().ToString().Replace("-", string.Empty);
					await RuntimeSettings.SetAsync("DeviceId", this.deviceId);
				}

				Log.Informational("Device ID: " + this.deviceId);

				/************************************************************************************
				 * To create an unencrypted CoAP Endpoint on the default CoAP port:
				 * 
				 *    this.coapEndpoint = new CoapEndpoint();
				 *    
				 * To create an unencrypted CoAP Endpoint on the default CoAP port, 
				 * with a sniffer that outputs communication to the window:
				 * 
				 *    this.coapEndpoint = new CoapEndpoint(new LogSniffer());
				 * 
				 * To create a DTLS encrypted CoAP endpoint, on the default CoAPS port, using
				 * the users defined in the IUserSource users:
				 * 
				 *    this.coapEndpoint = new CoapEndpoint(CoapEndpoint.DefaultCoapsPort, this.users);
				 *
				 * To create a CoAP endpoint, that listens to both the default CoAP port, for
				 * unencrypted communication, and the default CoAPS port, for encrypted,
				 * authenticated and authorized communication, using
				 * the users defined in the IUserSource users. Only users having the given
				 * privilege (if not empty) will be authorized to access resources on the endpoint:
				 * 
				 *    this.coapEndpoint = new CoapEndpoint(new int[] { CoapEndpoint.DefaultCoapPort },
				 *    	new int[] { CoapEndpoint.DefaultCoapsPort }, this.users, "PRIVILEGE", false, false);
				 * 
				 ************************************************************************************/

				this.coapEndpoint = new CoapEndpoint(new int[] { CoapEndpoint.DefaultCoapPort },
					new int[] { CoapEndpoint.DefaultCoapsPort }, this.users, string.Empty, false, false);

				this.outputResource = this.coapEndpoint.Register("/Output", (req, resp) =>
				{
					string s;

					if (this.output.HasValue)
						s = this.output.Value ? "true" : "false";
					else
						s = "-";

					resp.Respond(CoapCode.Content, s, 64);

				}, async (req, resp) =>
				{
					try
					{
						string s = req.Decode() as string;
						if (s == null && req.Payload != null)
							s = Encoding.UTF8.GetString(req.Payload);

						if (s == null || !CommonTypes.TryParse(s, out bool Output))
							resp.RST(CoapCode.BadRequest);
						else
						{
							resp.Respond(CoapCode.Changed);
							await this.SetOutput(Output, req.From.ToString());
						}
					}
					catch (Exception ex)
					{
						Log.Critical(ex);
					}
				}, Notifications.Acknowledged, "Digital Output.", null, null,
					new int[] { PlainText.ContentFormatCode });

				this.outputResource?.TriggerAll(new TimeSpan(0, 1, 0));
			}
			catch (Exception ex)
			{
				Log.Emergency(ex);

				MessageDialog Dialog = new MessageDialog(ex.Message, "Error");
				await MainPage.Instance.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
					async () => await Dialog.ShowAsync());
			}
		}

		public class Users : IUserSource
		{
			public bool TryGetUser(string UserName, out IUser User)
			{
				if (UserName == "MIoT")
					User = new User();
				else
					User = null;

				return User != null;
			}
		}

		public class User : IUser
		{
			public string UserName => "MIoT";
			public string PasswordHash => instance.CalcHash("rox");
			public string PasswordHashType => "SHA-256";

			public bool HasPrivilege(string Privilege)
			{
				return false;
			}
		}

		private string CalcHash(string Password)
		{
			return Waher.Security.Hashes.ComputeSHA256HashString(Encoding.UTF8.GetBytes(Password + ":" + this.deviceId));
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
				this.output = On;

				Log.Informational("Setting Control Parameter.", string.Empty, Actor ?? "Windows user",
					new KeyValuePair<string, object>("Output", On));

				if (Actor != null)
					await MainPage.Instance.OutputSet(On);

				this.outputResource?.TriggerAll();
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
