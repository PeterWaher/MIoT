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
using Windows.Storage;
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
using Waher.Content.Images;
using Waher.Content.Markdown;
using Waher.Content.Markdown.Web;
using Waher.Events;
using Waher.Networking.HTTP;
using Waher.Persistence;
using Waher.Persistence.Files;
using Waher.Persistence.Filters;
using Waher.Runtime.Settings;
using Waher.Runtime.Inventory;
using Waher.Script;
using Waher.Security;
using Waher.Security.JWT;

namespace ActuatorHttp
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
		private HttpServer httpServer = null;
		private IUserSource users = new Users();
		private JwtFactory tokenFactory = new JwtFactory();
		private JwtAuthentication tokenAuthentication;
		private bool? output = null;

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
					typeof(ImageCodec).GetTypeInfo().Assembly,
					typeof(MarkdownDocument).GetTypeInfo().Assembly,
					typeof(MarkdownToHtmlConverter).GetTypeInfo().Assembly,
					typeof(Expression).GetTypeInfo().Assembly,
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

				this.tokenAuthentication = new JwtAuthentication(this.deviceId, this.users, this.tokenFactory);

				this.httpServer = new HttpServer();
				//this.httpServer = new HttpServer(new LogSniffer());

				StorageFile File = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/Root/favicon.ico"));
				string Root = File.Path;
				Root = Root.Substring(0, Root.Length - 11);
				this.httpServer.Register(new HttpFolderResource(string.Empty, Root, false, false, true, true));

				this.httpServer.Register("/", (req, resp) =>
				{
					throw new TemporaryRedirectException("/Index.md");
				});

				this.httpServer.Register("/Momentary", (req, resp) =>
				{
					resp.SetHeader("Cache-Control", "max-age=0, no-cache, no-store");

					if (req.Header.Accept != null)
					{
						switch (req.Header.Accept.GetBestContentType("text/xml", "application/xml", "application/json"))
						{
							case "text/xml":
							case "application/xml":
								this.ReturnMomentaryAsXml(req, resp);
								break;

							case "application/json":
								this.ReturnMomentaryAsJson(req, resp);
								break;

							default:
								throw new NotAcceptableException();
						}
					}
					else
						this.ReturnMomentaryAsXml(req, resp);
				}, this.tokenAuthentication);

				this.httpServer.Register("/Set", null, async (req, resp) =>
				{
					try
					{
						if (!req.HasData)
							throw new BadRequestException();
						
						string s = req.DecodeData() as string;
						if (s == null || !CommonTypes.TryParse(s, out bool OutputValue))
							throw new BadRequestException();

						if (req.Header.Accept != null)
						{
							switch (req.Header.Accept.GetBestContentType("text/xml", "application/xml", "application/json"))
							{
								case "text/xml":
								case "application/xml":
									await this.SetOutput(OutputValue, req.RemoteEndPoint);
									this.ReturnMomentaryAsXml(req, resp);
									break;

								case "application/json":
									await this.SetOutput(OutputValue, req.RemoteEndPoint);
									this.ReturnMomentaryAsJson(req, resp);
									break;

								default:
									throw new NotAcceptableException();
							}
						}
						else
						{
							await this.SetOutput(OutputValue, req.RemoteEndPoint);
							this.ReturnMomentaryAsXml(req, resp);
						}

						resp.SendResponse();
					}
					catch (Exception ex)
					{
						resp.SendResponse(ex);
					}
				}, false, this.tokenAuthentication);

				this.httpServer.Register("/Login", null, (req, resp) =>
				{
					if (!req.HasData || req.Session == null)
						throw new BadRequestException();

					object Obj = req.DecodeData();
					Dictionary<string, string> Form = Obj as Dictionary<string, string>;

					if (Form == null ||
						!Form.TryGetValue("UserName", out string UserName) ||
						!Form.TryGetValue("Password", out string Password))
					{
						throw new BadRequestException();
					}

					string From = null;

					if (req.Session.TryGetVariable("from", out Variable v))
						From = v.ValueObject as string;

					if (string.IsNullOrEmpty(From))
						From = "/Index.md";

					IUser User = this.Login(UserName, Password);
					if (User != null)
					{
						Log.Informational("User logged in.", UserName, req.RemoteEndPoint, "LoginSuccessful", EventLevel.Minor);

						req.Session["User"] = User;
						req.Session.Remove("LoginError");

						throw new SeeOtherException(From);
					}
					else
					{
						Log.Warning("Invalid login attempt.", UserName, req.RemoteEndPoint, "LoginFailure", EventLevel.Minor);
						req.Session["LoginError"] = "Invalid login credentials provided.";
					}

					throw new SeeOtherException(req.Header.Referer.Value);

				}, true, false, true);

				this.httpServer.Register("/GetSessionToken", null, (req, resp) =>
				{
					IUser User;

					if (!req.Session.TryGetVariable("User", out Variable v) ||
						(User = v.ValueObject as IUser) == null)
					{
						throw new ForbiddenException();
					}

					string Token = this.tokenFactory.Create(new KeyValuePair<string, object>("sub", User.UserName));

					resp.ContentType = JwtCodec.ContentType;
					resp.Write(Token);
				}, true, false, true);

			}
			catch (Exception ex)
			{
				Log.Emergency(ex);

				MessageDialog Dialog = new MessageDialog(ex.Message, "Error");
				await Dialog.ShowAsync();
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

		private IUser Login(string UserName, string Password)
		{
			if (this.users.TryGetUser(UserName, out IUser User))
			{
				switch (User.PasswordHashType)
				{
					case "":
						if (Password == User.PasswordHash)
							return User;
						break;

					case "SHA-256":
						if (this.CalcHash(Password) == User.PasswordHash)
							return User;
						break;

					default:
						Log.Error("Unsupported Hash function: " + User.PasswordHashType);
						break;
				}
			}

			return null;
		}

		private void ReturnMomentaryAsXml(HttpRequest Request, HttpResponse Response)
		{
			Response.ContentType = "application/xml";

			Response.Write("<?xml version='1.0' encoding='");
			Response.Write(Response.Encoding.WebName);
			Response.Write("'?>");

			string SchemaUrl = Request.Header.GetURL();
			int i = SchemaUrl.IndexOf("/Momentary");
			SchemaUrl = SchemaUrl.Substring(0, i) + "/schema.xsd";

			Response.Write("<Momentary timestamp='");
			Response.Write(DateTime.Now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
			Response.Write("' xmlns='");
			Response.Write(SchemaUrl);
			Response.Write("'>");

			if (this.output.HasValue)
			{
				Response.Write("<Output value='");
				Response.Write(this.output.Value ? "true" : "false");
				Response.Write("'/>");
			}

			Response.Write("</Momentary>");
		}

		private void ReturnMomentaryAsJson(HttpRequest Request, HttpResponse Response)
		{
			Response.ContentType = "application/json";

			Response.Write("{\"ts\":\"");
			Response.Write(DateTime.Now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
			Response.Write('"');

			if (this.output.HasValue)
			{
				Response.Write(",\"output\":");
				Response.Write(this.output.Value ? "true" : "false");
			}

			Response.Write('}');
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

			if (this.httpServer != null)
			{
				this.httpServer.Dispose();
				this.httpServer = null;
			}

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

		public static string Output
		{
			get
			{
				if (instance.output.HasValue)
					return instance.output.Value ? "ON" : "OFF";
				else
					return string.Empty;
			}
		}
	}
}
