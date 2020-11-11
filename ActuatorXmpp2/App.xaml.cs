//#define GPIO

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Devices.Gpio;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Storage;
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
using Waher.Content.Xml;
using Waher.Events;
using Waher.Networking.XMPP;
using Waher.Networking.XMPP.BitsOfBinary;
using Waher.Networking.XMPP.Chat;
using Waher.Networking.XMPP.Control;
using Waher.Networking.XMPP.Provisioning;
using Waher.Networking.XMPP.ServiceDiscovery;
using Waher.Networking.XMPP.Sensor;
using Waher.Security;
using Waher.Things;
using Waher.Things.ControlParameters;
using Waher.Things.SensorData;
using Waher.Persistence;
using Waher.Persistence.Files;
using Waher.Persistence.Serialization;
using Waher.Runtime.Settings;
using Waher.Runtime.Inventory;
using Windows.Devices.Midi;

namespace ActuatorXmpp
{
	/// <summary>
	/// Provides application-specific behavior to supplement the default Application class.
	/// </summary>
	sealed partial class App : Application
	{
		private static App instance = null;
		private FilesProvider db = null;

#if GPIO
		private const int gpioOutputPin = 5;
		private GpioController gpio = null;
		private GpioPin gpioPin = null;
#else
		private UsbSerial arduinoUsb = null;
		private RemoteDevice arduino = null;
#endif
		private string deviceId;
		private XmppClient xmppClient = null;
		private ControlServer controlServer = null;
		private SensorServer sensorServer = null;
		private ThingRegistryClient registryClient = null;
		private BobClient bobClient = null;
		private ChatServer chatServer = null;
		private ProvisioningClient provisioningClient = null;
		private Timer minuteTimer = null;
		private bool? output = null;
		private bool newXmppClient = true;

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
				Log.Informational("Starting application.");

				Types.Initialize(
					typeof(FilesProvider).GetTypeInfo().Assembly,
					typeof(ObjectSerializer).GetTypeInfo().Assembly,    // Waher.Persistence.Serialization was broken out of Waher.Persistence.FilesLW after the publishing of the MIoT book.
					typeof(RuntimeSettings).GetTypeInfo().Assembly,
					typeof(IContentEncoder).GetTypeInfo().Assembly,
					typeof(XmppClient).GetTypeInfo().Assembly,
					typeof(Waher.Content.Markdown.MarkdownDocument).GetTypeInfo().Assembly,
					typeof(XML).GetTypeInfo().Assembly,
					typeof(Waher.Script.Expression).GetTypeInfo().Assembly,
					typeof(Waher.Script.Graphs.Graph).GetTypeInfo().Assembly,
					typeof(Waher.Script.Persistence.SQL.Select).GetTypeInfo().Assembly,
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

							this.output = await RuntimeSettings.GetAsync("Actuator.Output", false);
							this.gpioPin.Write(this.output ? GpioPinValue.High : GpioPinValue.Low);

							await MainPage.Instance.OutputSet(this.output);

							Log.Informational("Setting Control Parameter.", string.Empty, "Startup",
								new KeyValuePair<string, object>("Output", this.output));
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

							this.output = await RuntimeSettings.GetAsync("Actuator.Output", false);
							this.arduino.digitalWrite(9, this.output.Value ? PinState.HIGH : PinState.LOW);

							await MainPage.Instance.OutputSet(this.output.Value);

							Log.Informational("Setting Control Parameter.", string.Empty, "Startup",
								new KeyValuePair<string, object>("Output", this.output));

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
				}
#endif
				this.deviceId = await RuntimeSettings.GetAsync("DeviceId", string.Empty);
				if (string.IsNullOrEmpty(this.deviceId))
				{
					this.deviceId = Guid.NewGuid().ToString().Replace("-", string.Empty);
					await RuntimeSettings.SetAsync("DeviceId", this.deviceId);
				}

				Log.Informational("Device ID: " + this.deviceId);

				string Host = await RuntimeSettings.GetAsync("XmppHost", "waher.se");
				int Port = (int)await RuntimeSettings.GetAsync("XmppPort", 5222);
				string UserName = await RuntimeSettings.GetAsync("XmppUserName", string.Empty);
				string PasswordHash = await RuntimeSettings.GetAsync("XmppPasswordHash", string.Empty);
				string PasswordHashMethod = await RuntimeSettings.GetAsync("XmppPasswordHashMethod", string.Empty);

				if (string.IsNullOrEmpty(Host) ||
					Port <= 0 || Port > ushort.MaxValue ||
					string.IsNullOrEmpty(UserName) ||
					string.IsNullOrEmpty(PasswordHash) ||
					string.IsNullOrEmpty(PasswordHashMethod))
				{
					await MainPage.Instance.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
						async () => await this.ShowConnectionDialog(Host, Port, UserName));
				}
				else
				{
					this.xmppClient = new XmppClient(Host, Port, UserName, PasswordHash, PasswordHashMethod, "en",
						typeof(App).GetTypeInfo().Assembly)     // Add "new LogSniffer()" to the end, to output communication to the log.
					{
						AllowCramMD5 = false,
						AllowDigestMD5 = false,
						AllowPlain = false,
						AllowScramSHA1 = true,
						AllowScramSHA256 = true
					};
					this.xmppClient.OnStateChanged += this.StateChanged;
					this.xmppClient.OnConnectionError += this.ConnectionError;

					Log.Informational("Connecting to " + this.xmppClient.Host + ":" + this.xmppClient.Port.ToString());
					this.xmppClient.Connect();
				}

				this.minuteTimer = new Timer((State) =>
				{
					if (this.xmppClient != null &&
						(this.xmppClient.State == XmppState.Error || this.xmppClient.State == XmppState.Offline))
					{
						this.xmppClient.Reconnect();
					}

				}, null, 60000, 60000);
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

		private async Task ShowConnectionDialog(string Host, int Port, string UserName)
		{
			try
			{
				AccountDialog Dialog = new AccountDialog(Host, Port, UserName);

				switch (await Dialog.ShowAsync())
				{
					case ContentDialogResult.Primary:
						if (this.xmppClient != null)
						{
							this.xmppClient.Dispose();
							this.xmppClient = null;
						}

						this.xmppClient = new XmppClient(Dialog.Host, Dialog.Port, Dialog.UserName, Dialog.Password, "en", typeof(App).GetTypeInfo().Assembly)
						{
							AllowCramMD5 = false,
							AllowDigestMD5 = false,
							AllowPlain = false,
							AllowScramSHA1 = true,
							AllowScramSHA256 = true
						};

						this.xmppClient.AllowRegistration();                // Allows registration on servers that do not require signatures.
																			// this.xmppClient.AllowRegistration(Key, Secret);	// Allows registration on servers requiring a signature of the registration request.

						this.xmppClient.OnStateChanged += this.TestConnectionStateChanged;
						this.xmppClient.OnConnectionError += this.ConnectionError;

						Log.Informational("Connecting to " + this.xmppClient.Host + ":" + this.xmppClient.Port.ToString());
						this.xmppClient.Connect();
						break;

					case ContentDialogResult.Secondary:
						break;
				}
			}
			catch (Exception ex)
			{
				Log.Critical(ex);
			}
		}

		private Task StateChanged(object _, XmppState State)
		{
			Log.Informational("Changing state: " + State.ToString());

			if (State == XmppState.Connected)
			{
				Log.Informational("Connected as " + this.xmppClient.FullJID);
				Task.Run(this.SetVCard);
				Task.Run(this.RegisterDevice);
			}

			return Task.CompletedTask;
		}

		private Task ConnectionError(object _, Exception ex)
		{
			Log.Error(ex.Message);
			return Task.CompletedTask;
		}

		private void AttachFeatures()
		{
			if (this.controlServer != null)
			{
				this.controlServer.Dispose();
				this.controlServer = null;
			}

			if (this.sensorServer != null)
			{
				this.sensorServer.Dispose();
				this.sensorServer = null;
			}

			this.controlServer = new ControlServer(this.xmppClient, this.provisioningClient,
				new BooleanControlParameter("Output", "Actuator", "Output:", "Digital output.",
					(Node) => Task.FromResult<bool?>(this.output),
					async (Node, Value) =>
					{
						try
						{
							await this.SetOutput(Value, "XMPP");
						}
						catch (Exception ex)
						{
							Log.Critical(ex);
						}
					}));

			this.sensorServer = new SensorServer(this.xmppClient, this.provisioningClient, true);
			this.sensorServer.OnExecuteReadoutRequest += (sender, e) =>
			{
				try
				{
					Log.Informational("Performing readout.", this.xmppClient.BareJID, e.Actor);

					List<Field> Fields = new List<Field>();
					DateTime Now = DateTime.Now;

					if (e.IsIncluded(FieldType.Identity))
						Fields.Add(new StringField(ThingReference.Empty, Now, "Device ID", this.deviceId, FieldType.Identity, FieldQoS.AutomaticReadout));

					if (this.output.HasValue)
					{
						Fields.Add(new BooleanField(ThingReference.Empty, Now, "Output", this.output.Value,
							FieldType.Momentary, FieldQoS.AutomaticReadout, true));
					}

					e.ReportFields(true, Fields);
				}
				catch (Exception ex)
				{
					e.ReportErrors(true, new ThingError(ThingReference.Empty, ex.Message));
				}

				return Task.CompletedTask;
			};

			if (this.newXmppClient)
			{
				this.newXmppClient = false;

				this.xmppClient.OnError += (Sender, ex) =>
				{
					Log.Error(ex);
					return Task.CompletedTask;
				};

				this.xmppClient.OnPasswordChanged += (Sender, e) => Log.Informational("Password changed.", this.xmppClient.BareJID);

				this.xmppClient.OnPresenceSubscribed += (Sender, e) =>
				{
					Log.Informational("Friendship request accepted.", this.xmppClient.BareJID, e.From);
					return Task.CompletedTask;
				};

				this.xmppClient.OnPresenceUnsubscribed += (Sender, e) =>
				{
					Log.Informational("Friendship removal accepted.", this.xmppClient.BareJID, e.From);
					return Task.CompletedTask;
				};

				if (this.bobClient is null)
					this.bobClient = new BobClient(this.xmppClient, Path.Combine(Path.GetTempPath(), "BitsOfBinary"));

				if (this.chatServer != null)
				{
					this.chatServer.Dispose();
					this.chatServer = null;
				}

				this.chatServer = new ChatServer(this.xmppClient, this.bobClient, this.sensorServer, this.controlServer, this.provisioningClient);

				// XEP-0054: vcard-temp: http://xmpp.org/extensions/xep-0054.html
				this.xmppClient.RegisterIqGetHandler("vCard", "vcard-temp", this.QueryVCardHandler, true);
			}
		}

		private async Task TestConnectionStateChanged(object Sender, XmppState State)
		{
			Log.Informational("Changing state: " + State.ToString());

			switch (State)
			{
				case XmppState.Connected:
					await RuntimeSettings.SetAsync("XmppHost", this.xmppClient.Host);
					await RuntimeSettings.SetAsync("XmppPort", this.xmppClient.Port);
					await RuntimeSettings.SetAsync("XmppUserName", this.xmppClient.UserName);
					await RuntimeSettings.SetAsync("XmppPasswordHash", this.xmppClient.PasswordHash);
					await RuntimeSettings.SetAsync("XmppPasswordHashMethod", this.xmppClient.PasswordHashMethod);

					this.xmppClient.OnStateChanged -= this.TestConnectionStateChanged;
					this.xmppClient.OnStateChanged += this.StateChanged;
					await this.SetVCard();
					await this.RegisterDevice();
					break;

				case XmppState.Error:
				case XmppState.Offline:
					if (!(this.xmppClient is null))
					{
						await MainPage.Instance.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
							async () => await this.ShowConnectionDialog(this.xmppClient.Host, this.xmppClient.Port, this.xmppClient.UserName));
					}
					break;
			}
		}

		private async Task QueryVCardHandler(object Sender, IqEventArgs e)
		{
			e.IqResult(await this.GetVCardXml());
		}

		private async Task SetVCard()
		{
			Log.Informational("Setting vCard");

			// XEP-0054 - vcard-temp: http://xmpp.org/extensions/xep-0054.html

			this.xmppClient.SendIqSet(string.Empty, await this.GetVCardXml(), (sender, e) =>
			{
				if (e.Ok)
					Log.Informational("vCard successfully set.");
				else
					Log.Error("Unable to set vCard.");

				return Task.CompletedTask;

			}, null);
		}

		private async Task<string> GetVCardXml()
		{
			StringBuilder Xml = new StringBuilder();

			Xml.Append("<vCard xmlns='vcard-temp'>");
			Xml.Append("<FN>MIoT Actuator</FN><N><FAMILY>Actuator</FAMILY><GIVEN>MIoT</GIVEN><MIDDLE/></N>");
			Xml.Append("<URL>https://github.com/PeterWaher/MIoT</URL>");
			Xml.Append("<JABBERID>");
			Xml.Append(XML.Encode(this.xmppClient.BareJID));
			Xml.Append("</JABBERID>");
			Xml.Append("<UID>");
			Xml.Append(this.deviceId);
			Xml.Append("</UID>");
			Xml.Append("<DESC>XMPP Actuator Project (ActuatorXmpp) from the book Mastering Internet of Things, by Peter Waher.</DESC>");

			// XEP-0153 - vCard-Based Avatars: http://xmpp.org/extensions/xep-0153.html

			StorageFile File = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/LargeTile.scale-100.png"));
			byte[] Icon = System.IO.File.ReadAllBytes(File.Path);

			Xml.Append("<PHOTO><TYPE>image/png</TYPE><BINVAL>");
			Xml.Append(Convert.ToBase64String(Icon));
			Xml.Append("</BINVAL></PHOTO>");
			Xml.Append("</vCard>");

			return Xml.ToString();
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
				this.output = On;

				this.sensorServer?.NewMomentaryValues(new BooleanField(ThingReference.Empty, DateTime.Now, "Output", On,
					FieldType.Momentary, FieldQoS.AutomaticReadout));

				Log.Informational("Setting Control Parameter.", string.Empty, Actor ?? "Windows user",
					new KeyValuePair<string, object>("Output", On));

				if (Actor != null)
					await MainPage.Instance.OutputSet(On);
			}
		}

		private async Task RegisterDevice()
		{
			string ThingRegistryJid = await RuntimeSettings.GetAsync("ThingRegistry.JID", string.Empty);
			string ProvisioningJid = await RuntimeSettings.GetAsync("ProvisioningServer.JID", ThingRegistryJid);
			string OwnerJid = await RuntimeSettings.GetAsync("ThingRegistry.Owner", string.Empty);

			if (!string.IsNullOrEmpty(ThingRegistryJid) && !string.IsNullOrEmpty(ProvisioningJid))
			{
				this.UseProvisioningServer(ProvisioningJid, OwnerJid);
				await this.RegisterDevice(ThingRegistryJid, OwnerJid);
			}
			else
			{
				Log.Informational("Searching for Thing Registry and Provisioning Server.");

				this.xmppClient.SendServiceItemsDiscoveryRequest(this.xmppClient.Domain, (sender, e) =>
				{
					foreach (Item Item in e.Items)
					{
						this.xmppClient.SendServiceDiscoveryRequest(Item.JID, async (sender2, e2) =>
						{
							try
							{
								Item Item2 = (Item)e2.State;

								if (e2.HasFeature(ProvisioningClient.NamespaceProvisioningDevice))
								{
									Log.Informational("Provisioning server found.", Item2.JID);
									this.UseProvisioningServer(Item2.JID, OwnerJid);
									await RuntimeSettings.SetAsync("ProvisioningServer.JID", Item2.JID);
								}

								if (e2.HasFeature(ThingRegistryClient.NamespaceDiscovery))
								{
									Log.Informational("Thing registry found.", Item2.JID);

									await RuntimeSettings.SetAsync("ThingRegistry.JID", Item2.JID);
									await this.RegisterDevice(Item2.JID, OwnerJid);
								}
							}
							catch (Exception ex)
							{
								Log.Critical(ex);
							}
						}, Item);
					}

					return Task.CompletedTask;

				}, null);
			}
		}

		private void UseProvisioningServer(string JID, string OwnerJid)
		{
			if (this.provisioningClient is null ||
				this.provisioningClient.ProvisioningServerAddress != JID ||
				this.provisioningClient.OwnerJid != OwnerJid)
			{
				if (this.provisioningClient != null)
				{
					this.provisioningClient.Dispose();
					this.provisioningClient = null;
				}

				this.provisioningClient = new ProvisioningClient(this.xmppClient, JID, OwnerJid);
				this.AttachFeatures();
			}
		}

		private async Task RegisterDevice(string RegistryJid, string OwnerJid)
		{
			if (this.registryClient is null || this.registryClient.ThingRegistryAddress != RegistryJid)
			{
				if (this.registryClient != null)
				{
					this.registryClient.Dispose();
					this.registryClient = null;
				}

				this.registryClient = new ThingRegistryClient(this.xmppClient, RegistryJid);

				this.registryClient.Claimed += async (sender, e) =>
				{
					try
					{
						await RuntimeSettings.SetAsync("ThingRegistry.Owner", e.JID);
						await RuntimeSettings.SetAsync("ThingRegistry.Key", string.Empty);
					}
					catch (Exception ex)
					{
						Log.Critical(ex);
					}
				};

				this.registryClient.Disowned += async (sender, e) =>
				{
					try
					{
						await RuntimeSettings.SetAsync("ThingRegistry.Owner", string.Empty);
						await this.RegisterDevice();
					}
					catch (Exception ex)
					{
						Log.Critical(ex);
					}
				};
			}

			string s;
			List<MetaDataTag> MetaInfo = new List<MetaDataTag>()
			{
				new MetaDataStringTag("CLASS", "Actuator"),
				new MetaDataStringTag("TYPE", "MIoT Actuator"),
				new MetaDataStringTag("MAN", "waher.se"),
				new MetaDataStringTag("MODEL", "MIoT ActuatorXmpp2"),
				new MetaDataStringTag("PURL", "https://github.com/PeterWaher/MIoT"),
				new MetaDataStringTag("SN", this.deviceId),
				new MetaDataNumericTag("V", 2.0)
			};

			if (await RuntimeSettings.GetAsync("ThingRegistry.Location", false))
			{
				s = await RuntimeSettings.GetAsync("ThingRegistry.Country", string.Empty);
				if (!string.IsNullOrEmpty(s))
					MetaInfo.Add(new MetaDataStringTag("COUNTRY", s));

				s = await RuntimeSettings.GetAsync("ThingRegistry.Region", string.Empty);
				if (!string.IsNullOrEmpty(s))
					MetaInfo.Add(new MetaDataStringTag("REGION", s));

				s = await RuntimeSettings.GetAsync("ThingRegistry.City", string.Empty);
				if (!string.IsNullOrEmpty(s))
					MetaInfo.Add(new MetaDataStringTag("CITY", s));

				s = await RuntimeSettings.GetAsync("ThingRegistry.Area", string.Empty);
				if (!string.IsNullOrEmpty(s))
					MetaInfo.Add(new MetaDataStringTag("AREA", s));

				s = await RuntimeSettings.GetAsync("ThingRegistry.Street", string.Empty);
				if (!string.IsNullOrEmpty(s))
					MetaInfo.Add(new MetaDataStringTag("STREET", s));

				s = await RuntimeSettings.GetAsync("ThingRegistry.StreetNr", string.Empty);
				if (!string.IsNullOrEmpty(s))
					MetaInfo.Add(new MetaDataStringTag("STREETNR", s));

				s = await RuntimeSettings.GetAsync("ThingRegistry.Building", string.Empty);
				if (!string.IsNullOrEmpty(s))
					MetaInfo.Add(new MetaDataStringTag("BLD", s));

				s = await RuntimeSettings.GetAsync("ThingRegistry.Apartment", string.Empty);
				if (!string.IsNullOrEmpty(s))
					MetaInfo.Add(new MetaDataStringTag("APT", s));

				s = await RuntimeSettings.GetAsync("ThingRegistry.Room", string.Empty);
				if (!string.IsNullOrEmpty(s))
					MetaInfo.Add(new MetaDataStringTag("ROOM", s));

				s = await RuntimeSettings.GetAsync("ThingRegistry.Name", string.Empty);
				if (!string.IsNullOrEmpty(s))
					MetaInfo.Add(new MetaDataStringTag("NAME", s));

				await this.UpdateRegistration(MetaInfo.ToArray(), OwnerJid);
			}
			else
			{
				try
				{
					await MainPage.Instance.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, async () =>
					{
						try
						{
							RegistrationDialog Dialog = new RegistrationDialog();

							switch (await Dialog.ShowAsync())
							{
								case ContentDialogResult.Primary:
									await RuntimeSettings.SetAsync("ThingRegistry.Country", s = Dialog.Reg_Country);
									if (!string.IsNullOrEmpty(s))
										MetaInfo.Add(new MetaDataStringTag("COUNTRY", s));

									await RuntimeSettings.SetAsync("ThingRegistry.Region", s = Dialog.Reg_Region);
									if (!string.IsNullOrEmpty(s))
										MetaInfo.Add(new MetaDataStringTag("REGION", s));

									await RuntimeSettings.SetAsync("ThingRegistry.City", s = Dialog.Reg_City);
									if (!string.IsNullOrEmpty(s))
										MetaInfo.Add(new MetaDataStringTag("CITY", s));

									await RuntimeSettings.SetAsync("ThingRegistry.Area", s = Dialog.Reg_Area);
									if (!string.IsNullOrEmpty(s))
										MetaInfo.Add(new MetaDataStringTag("AREA", s));

									await RuntimeSettings.SetAsync("ThingRegistry.Street", s = Dialog.Reg_Street);
									if (!string.IsNullOrEmpty(s))
										MetaInfo.Add(new MetaDataStringTag("STREET", s));

									await RuntimeSettings.SetAsync("ThingRegistry.StreetNr", s = Dialog.Reg_StreetNr);
									if (!string.IsNullOrEmpty(s))
										MetaInfo.Add(new MetaDataStringTag("STREETNR", s));

									await RuntimeSettings.SetAsync("ThingRegistry.Building", s = Dialog.Reg_Building);
									if (!string.IsNullOrEmpty(s))
										MetaInfo.Add(new MetaDataStringTag("BLD", s));

									await RuntimeSettings.SetAsync("ThingRegistry.Apartment", s = Dialog.Reg_Apartment);
									if (!string.IsNullOrEmpty(s))
										MetaInfo.Add(new MetaDataStringTag("APT", s));

									await RuntimeSettings.SetAsync("ThingRegistry.Room", s = Dialog.Reg_Room);
									if (!string.IsNullOrEmpty(s))
										MetaInfo.Add(new MetaDataStringTag("ROOM", s));

									await RuntimeSettings.SetAsync("ThingRegistry.Name", s = Dialog.Name);
									if (!string.IsNullOrEmpty(s))
										MetaInfo.Add(new MetaDataStringTag("NAME", s));

									await this.RegisterDevice(MetaInfo.ToArray());
									break;

								case ContentDialogResult.Secondary:
									await this.RegisterDevice();
									break;
							}
						}
						catch (Exception ex)
						{
							Log.Critical(ex);
						}
					});
				}
				catch (Exception ex)
				{
					Log.Critical(ex);
				}
			}
		}

		private async Task RegisterDevice(MetaDataTag[] MetaInfo)
		{
			string Key = await RuntimeSettings.GetAsync("ThingRegistry.Key", string.Empty);
			if (string.IsNullOrEmpty(Key))
			{
				byte[] Bin = new byte[32];
				using (RandomNumberGenerator Rnd = RandomNumberGenerator.Create())
				{
					Rnd.GetBytes(Bin);
				}

				Key = Hashes.BinaryToString(Bin);
				await RuntimeSettings.SetAsync("ThingRegistry.Key", Key);
			}

			int c = MetaInfo.Length;
			Array.Resize<MetaDataTag>(ref MetaInfo, c + 1);
			MetaInfo[c] = new MetaDataStringTag("KEY", Key);

			this.registryClient.RegisterThing(false, MetaInfo, async (sender, e) =>
			{
				try
				{
					if (e.Ok)
					{
						await RuntimeSettings.SetAsync("ThingRegistry.Location", true);
						await RuntimeSettings.SetAsync("ThingRegistry.Owner", e.OwnerJid);

						if (string.IsNullOrEmpty(e.OwnerJid))
						{
							string ClaimUrl = registryClient.EncodeAsIoTDiscoURI(MetaInfo);
							string FilePath = ApplicationData.Current.LocalFolder.Path + Path.DirectorySeparatorChar + "Actuator.iotdisco";

							Log.Informational("Registration successful.");
							Log.Informational(ClaimUrl, new KeyValuePair<string, object>("Path", FilePath));

							File.WriteAllText(FilePath, ClaimUrl);
						}
						else
						{
							await RuntimeSettings.SetAsync("ThingRegistry.Key", string.Empty);
							Log.Informational("Registration updated. Device has an owner.", new KeyValuePair<string, object>("Owner", e.OwnerJid));
						}
					}
					else
					{
						Log.Error("Registration failed.");
						await this.RegisterDevice();
					}
				}
				catch (Exception ex)
				{
					Log.Critical(ex);
				}
			}, null);
		}

		private async Task UpdateRegistration(MetaDataTag[] MetaInfo, string OwnerJid)
		{
			if (string.IsNullOrEmpty(OwnerJid))
				await this.RegisterDevice(MetaInfo);
			else
			{
				Log.Informational("Updating registration of device.",
					new KeyValuePair<string, object>("Owner", OwnerJid));

				this.registryClient.UpdateThing(MetaInfo, async (sender, e) =>
				{
					try
					{
						if (e.Disowned)
						{
							await RuntimeSettings.SetAsync("ThingRegistry.Owner", string.Empty);
							await this.RegisterDevice(MetaInfo);
						}
						else if (e.Ok)
							Log.Informational("Registration update successful.");
						else
						{
							Log.Error("Registration update failed.");
							await this.RegisterDevice(MetaInfo);
						}
					}
					catch (Exception ex)
					{
						Log.Critical(ex);
					}
				}, null);
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

			this.chatServer?.Dispose();
			this.chatServer = null;

			this.bobClient?.Dispose();
			this.bobClient = null;

			this.sensorServer?.Dispose();
			this.sensorServer = null;

			this.xmppClient?.Dispose();
			this.xmppClient = null;

			this.minuteTimer?.Dispose();
			this.minuteTimer = null;

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
