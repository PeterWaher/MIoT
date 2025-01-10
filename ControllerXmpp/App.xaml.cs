using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Waher.Content;
using Waher.Content.Xml;
using Waher.Events;
using Waher.Networking.XMPP;
using Waher.Networking.XMPP.BitsOfBinary;
using Waher.Networking.XMPP.Chat;
using Waher.Networking.XMPP.Control;
using Waher.Networking.XMPP.Provisioning;
using Waher.Networking.XMPP.Provisioning.SearchOperators;
using Waher.Networking.XMPP.Sensor;
using Waher.Networking.XMPP.ServiceDiscovery;
using Waher.Persistence;
using Waher.Persistence.Files;
using Waher.Persistence.Serialization;
using Waher.Runtime.Inventory;
using Waher.Runtime.Settings;
using Waher.Things;
using Waher.Things.SensorData;
using Waher.Networking.XMPP.Events;

namespace ControllerXmpp
{
	/// <summary>
	/// Provides application-specific behavior to supplement the default Application class.
	/// </summary>
	sealed partial class App : Application
	{
		private FilesProvider db = null;
		private Timer secondTimer = null;
		private XmppClient xmppClient = null;
		private BobClient bobClient = null;
		private ChatServer chatServer = null;
		private SensorClient sensorClient = null;
		private ControlClient controlClient = null;
		private SensorServer sensorServer = null;
		private ThingRegistryClient registryClient = null;
		private string sensorJid = null;
		private string actuatorJid = null;
		private ThingReference sensor = null;
		private ThingReference actuator = null;
		private SensorDataSubscriptionRequest subscription = null;
		private double? light = null;
		private bool? motion = null;
		private bool? output = null;
		private DateTime lastEventFields = DateTime.Now;
		private DateTime lastEventErrors = DateTime.Now;
		private DateTime lastOutput = DateTime.Now;
		private DateTime lastRequestActuator = DateTime.MinValue;
		private DateTime lastFindFriends = DateTime.MinValue;
		private string deviceId;

		/// <summary>
		/// Initializes the singleton application object.  This is the first line of authored code
		/// executed, and as such is the logical equivalent of main() or WinMain().
		/// </summary>
		public App()
		{
			this.InitializeComponent();
			this.Suspending += this.OnSuspending;
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

				rootFrame.NavigationFailed += this.OnNavigationFailed;

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
					typeof(IContentEncoder).GetTypeInfo().Assembly,
					typeof(XmppClient).GetTypeInfo().Assembly,
					typeof(Waher.Content.Markdown.MarkdownDocument).GetTypeInfo().Assembly,
					typeof(XML).GetTypeInfo().Assembly,
					typeof(Waher.Script.Expression).GetTypeInfo().Assembly,
					typeof(Waher.Script.Graphs.Graph).GetTypeInfo().Assembly,
					typeof(Waher.Script.Persistence.SQL.Select).GetTypeInfo().Assembly,
					typeof(App).GetTypeInfo().Assembly);

				this.db = await FilesProvider.CreateAsync(Windows.Storage.ApplicationData.Current.LocalFolder.Path +
					Path.DirectorySeparatorChar + "Data", "Default", 8192, 1000, 8192, Encoding.UTF8, 10000);
				Database.Register(this.db);
				await this.db.RepairIfInproperShutdown(null);
				await this.db.Start();

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
					this.xmppClient.OnRosterItemAdded += this.XmppClient_OnRosterItemAdded;
					this.xmppClient.OnRosterItemUpdated += this.XmppClient_OnRosterItemUpdated;
					this.xmppClient.OnRosterItemRemoved += this.XmppClient_OnRosterItemRemoved;
					this.AttachFeatures();

					Log.Informational("Connecting to " + this.xmppClient.Host + ":" + this.xmppClient.Port.ToString());
					await this.xmppClient.Connect();
				}

				this.secondTimer = new Timer(this.SecondTimerCallback, null, 1000, 1000);
			}
			catch (Exception ex)
			{
				Log.Emergency(ex);

				MessageDialog Dialog = new MessageDialog(ex.Message, "Error");
				await MainPage.Instance.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
					async () => await Dialog.ShowAsync());
			}
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
							await this.xmppClient.DisposeAsync();
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
						this.xmppClient.OnRosterItemAdded += this.XmppClient_OnRosterItemAdded;
						this.xmppClient.OnRosterItemUpdated += this.XmppClient_OnRosterItemUpdated;
						this.xmppClient.OnRosterItemRemoved += this.XmppClient_OnRosterItemRemoved;

						Log.Informational("Connecting to " + this.xmppClient.Host + ":" + this.xmppClient.Port.ToString());
						await this.xmppClient.Connect();
						break;

					case ContentDialogResult.Secondary:
						break;
				}
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
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
			this.sensorServer = new SensorServer(this.xmppClient, true);
			this.sensorServer.OnExecuteReadoutRequest += (sender, e) =>
			{
				try
				{
					Log.Informational("Performing readout.", this.xmppClient.BareJID, e.Actor);

					List<Field> Fields = new List<Field>();
					DateTime Now = DateTime.Now;

					if (e.IsIncluded(FieldType.Identity))
					{
						Fields.Add(new StringField(ThingReference.Empty, Now, "Device ID", this.deviceId, FieldType.Identity, FieldQoS.AutomaticReadout));

						if (!string.IsNullOrEmpty(this.sensorJid))
						{
							Fields.Add(new StringField(ThingReference.Empty, Now, "Sensor, JID", this.sensorJid, FieldType.Identity, FieldQoS.AutomaticReadout));

							if (this.sensor != null)
							{
								if (!string.IsNullOrEmpty(this.sensor.NodeId))
									Fields.Add(new StringField(ThingReference.Empty, Now, "Sensor, Node ID", this.sensor.NodeId, FieldType.Identity, FieldQoS.AutomaticReadout));

								if (!string.IsNullOrEmpty(this.sensor.SourceId))
									Fields.Add(new StringField(ThingReference.Empty, Now, "Sensor, Source ID", this.sensor.SourceId, FieldType.Identity, FieldQoS.AutomaticReadout));

								if (!string.IsNullOrEmpty(this.sensor.Partition))
									Fields.Add(new StringField(ThingReference.Empty, Now, "Sensor, Partition", this.sensor.Partition, FieldType.Identity, FieldQoS.AutomaticReadout));
							}
						}

						if (!string.IsNullOrEmpty(this.actuatorJid))
						{
							Fields.Add(new StringField(ThingReference.Empty, Now, "Actuator, JID", this.actuatorJid, FieldType.Identity, FieldQoS.AutomaticReadout));

							if (this.actuator != null)
							{
								if (!string.IsNullOrEmpty(this.actuator.NodeId))
									Fields.Add(new StringField(ThingReference.Empty, Now, "Actuator, Node ID", this.actuator.NodeId, FieldType.Identity, FieldQoS.AutomaticReadout));

								if (!string.IsNullOrEmpty(this.actuator.SourceId))
									Fields.Add(new StringField(ThingReference.Empty, Now, "Actuator, Source ID", this.actuator.SourceId, FieldType.Identity, FieldQoS.AutomaticReadout));

								if (!string.IsNullOrEmpty(this.actuator.Partition))
									Fields.Add(new StringField(ThingReference.Empty, Now, "Actuator, Partition", this.actuator.Partition, FieldType.Identity, FieldQoS.AutomaticReadout));
							}
						}
					}

					if (e.IsIncluded(FieldType.Status))
					{
						RosterItem Item;

						if (string.IsNullOrEmpty(this.sensorJid))
							Fields.Add(new StringField(ThingReference.Empty, Now, "Sensor, Availability", "Not Found", FieldType.Status, FieldQoS.AutomaticReadout));
						else
						{
							Item = this.xmppClient[this.sensorJid];
							if (Item is null)
								Fields.Add(new StringField(ThingReference.Empty, Now, "Sensor, Availability", "Not Found", FieldType.Status, FieldQoS.AutomaticReadout));
							else if (!Item.HasLastPresence)
								Fields.Add(new StringField(ThingReference.Empty, Now, "Sensor, Availability", "Offline", FieldType.Status, FieldQoS.AutomaticReadout));
							else
								Fields.Add(new StringField(ThingReference.Empty, Now, "Sensor, Availability", Item.LastPresence.Availability.ToString(), FieldType.Status, FieldQoS.AutomaticReadout));
						}

						if (string.IsNullOrEmpty(this.actuatorJid))
							Fields.Add(new StringField(ThingReference.Empty, Now, "Actuator, Availability", "Not Found", FieldType.Status, FieldQoS.AutomaticReadout));
						else
						{
							Item = this.xmppClient[this.actuatorJid];
							if (Item is null)
								Fields.Add(new StringField(ThingReference.Empty, Now, "Actuator, Availability", "Not Found", FieldType.Status, FieldQoS.AutomaticReadout));
							else if (!Item.HasLastPresence)
								Fields.Add(new StringField(ThingReference.Empty, Now, "Actuator, Availability", "Offline", FieldType.Status, FieldQoS.AutomaticReadout));
							else
								Fields.Add(new StringField(ThingReference.Empty, Now, "Actuator, Availability", Item.LastPresence.Availability.ToString(), FieldType.Status, FieldQoS.AutomaticReadout));
						}
					}

					if (e.IsIncluded(FieldType.Momentary))
					{
						if (this.light.HasValue)
							Fields.Add(new QuantityField(ThingReference.Empty, Now, "Light", this.light.Value, 2, "%", FieldType.Momentary, FieldQoS.AutomaticReadout));

						if (this.motion.HasValue)
							Fields.Add(new BooleanField(ThingReference.Empty, Now, "Motion", this.motion.Value, FieldType.Momentary, FieldQoS.AutomaticReadout));

						if (this.output.HasValue)
							Fields.Add(new BooleanField(ThingReference.Empty, Now, "Output", this.output.Value, FieldType.Momentary, FieldQoS.AutomaticReadout));
					}

					e.ReportFields(true, Fields);
				}
				catch (Exception ex)
				{
					e.ReportErrors(true, new ThingError(ThingReference.Empty, ex.Message));
				}

				return Task.CompletedTask;
			};

			this.xmppClient.OnError += (Sender, ex) =>
			{
				Log.Error(ex);
				return Task.CompletedTask;
			};

			this.xmppClient.OnPasswordChanged += (Sender, e) =>
			{
				Log.Informational("Password changed.", this.xmppClient.BareJID);
				return Task.CompletedTask;
			};

			this.xmppClient.OnPresenceSubscribe += (Sender, e) =>
			{
				Log.Informational("Accepting friendship request.", this.xmppClient.BareJID, e.From);
				return e.Accept();
			};

			this.xmppClient.OnPresenceUnsubscribe += (Sender, e) =>
			{
				Log.Informational("Friendship removed.", this.xmppClient.BareJID, e.From);
				return e.Accept();
			};

			this.xmppClient.OnPresenceSubscribed += async (Sender, e) =>
			{
				Log.Informational("Friendship request accepted.", this.xmppClient.BareJID, e.From);

				if (string.Compare(e.FromBareJID, this.sensorJid, true) == 0)
					await this.SubscribeToSensorData();
			};

			this.xmppClient.OnPresenceUnsubscribed += (Sender, e) =>
			{
				Log.Informational("Friendship removal accepted.", this.xmppClient.BareJID, e.From);
				return Task.CompletedTask;
			};

			this.xmppClient.OnPresence += this.XmppClient_OnPresence;

			this.bobClient = new BobClient(this.xmppClient, Path.Combine(Path.GetTempPath(), "BitsOfBinary"));
			this.chatServer = new ChatServer(this.xmppClient, this.bobClient, this.sensorServer);

			// XEP-0054: vcard-temp: http://xmpp.org/extensions/xep-0054.html
			this.xmppClient.RegisterIqGetHandler("vCard", "vcard-temp", this.QueryVCardHandler, true);

			this.sensorClient = new SensorClient(this.xmppClient);
			this.controlClient = new ControlClient(this.xmppClient);
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
					this.AttachFeatures();
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
			await e.IqResult(await this.GetVCardXml());
		}

		private async Task SetVCard()
		{
			Log.Informational("Setting vCard");

			// XEP-0054 - vcard-temp: http://xmpp.org/extensions/xep-0054.html

			await this.xmppClient.SendIqSet(string.Empty, await this.GetVCardXml(), (sender, e) =>
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
			Xml.Append("<FN>MIoT Controller</FN><N><FAMILY>Controller</FAMILY><GIVEN>MIoT</GIVEN><MIDDLE/></N>");
			Xml.Append("<URL>https://github.com/PeterWaher/MIoT</URL>");
			Xml.Append("<JABBERID>");
			Xml.Append(XML.Encode(this.xmppClient.BareJID));
			Xml.Append("</JABBERID>");
			Xml.Append("<UID>");
			Xml.Append(this.deviceId);
			Xml.Append("</UID>");
			Xml.Append("<DESC>XMPP Controller Project (ControllerXmpp) from the book Mastering Internet of Things, by Peter Waher.</DESC>");

			// XEP-0153 - vCard-Based Avatars: http://xmpp.org/extensions/xep-0153.html

			StorageFile File = await StorageFile.GetFileFromApplicationUriAsync(new Uri("ms-appx:///Assets/LargeTile.scale-100.png"));
			byte[] Icon = System.IO.File.ReadAllBytes(File.Path);

			Xml.Append("<PHOTO><TYPE>image/png</TYPE><BINVAL>");
			Xml.Append(Convert.ToBase64String(Icon));
			Xml.Append("</BINVAL></PHOTO>");
			Xml.Append("</vCard>");

			return Xml.ToString();
		}

		private async Task RegisterDevice()
		{
			string ThingRegistryJid = await RuntimeSettings.GetAsync("ThingRegistry.JID", string.Empty);

			if (!string.IsNullOrEmpty(ThingRegistryJid))
				await this.RegisterDevice(ThingRegistryJid);
			else
			{
				Log.Informational("Searching for Thing Registry.");

				await this.xmppClient.SendServiceItemsDiscoveryRequest(this.xmppClient.Domain, (sender, e) =>
				{
					foreach (Item Item in e.Items)
					{
						this.xmppClient.SendServiceDiscoveryRequest(Item.JID, async (sender2, e2) =>
						{
							try
							{
								Item Item2 = (Item)e2.State;

								if (e2.HasAnyFeature(ThingRegistryClient.NamespacesDiscovery))
								{
									Log.Informational("Thing registry found.", Item2.JID);

									await RuntimeSettings.SetAsync("ThingRegistry.JID", Item2.JID);
									await this.RegisterDevice(Item2.JID);
								}
							}
							catch (Exception ex)
							{
								Log.Exception(ex);
							}
						}, Item);
					}

					return Task.CompletedTask;

				}, null);
			}
		}

		private async Task RegisterDevice(string RegistryJid)
		{
			if (this.registryClient is null || this.registryClient.ThingRegistryAddress != RegistryJid)
			{
				if (this.registryClient != null)
				{
					this.registryClient.Dispose();
					this.registryClient = null;
				}

				this.registryClient = new ThingRegistryClient(this.xmppClient, RegistryJid);
			}

			string s;
			List<MetaDataTag> MetaInfo = new List<MetaDataTag>()
			{
				new MetaDataStringTag("CLASS", "Controller"),
				new MetaDataStringTag("TYPE", "MIoT Controller"),
				new MetaDataStringTag("MAN", "waher.se"),
				new MetaDataStringTag("MODEL", "MIoT ControllerXmpp"),
				new MetaDataStringTag("PURL", "https://github.com/PeterWaher/MIoT"),
				new MetaDataStringTag("SN", this.deviceId),
				new MetaDataNumericTag("V", 1.0)
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

				this.UpdateRegistration(MetaInfo.ToArray());
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

									this.RegisterDevice(MetaInfo.ToArray());
									break;

								case ContentDialogResult.Secondary:
									await this.RegisterDevice();
									break;
							}
						}
						catch (Exception ex)
						{
							Log.Exception(ex);
						}
					});
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
			}
		}

		private void RegisterDevice(MetaDataTag[] MetaInfo)
		{
			Log.Informational("Registering device.");

			this.registryClient.RegisterThing(true, MetaInfo, async (sender, e) =>
			{
				try
				{
					if (e.Ok)
					{
						Log.Informational("Registration successful.");

						await RuntimeSettings.SetAsync("ThingRegistry.Location", true);
						await this.FindFriends(MetaInfo);
					}
					else
					{
						Log.Error("Registration failed.");
						await this.RegisterDevice();
					}
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
			}, null);
		}

		private void UpdateRegistration(MetaDataTag[] MetaInfo)
		{
			Log.Informational("Updating registration of device.");

			this.registryClient.UpdateThing(MetaInfo, async (sender, e) =>
			{
				if (e.Ok)
					Log.Informational("Registration update successful.");
				else
				{
					Log.Error("Registration update failed.");
					this.RegisterDevice(MetaInfo);
				}

				await this.FindFriends(MetaInfo);

			}, null);
		}

		private async Task FindFriends(MetaDataTag[] MetaInfo)
		{
			double ms = (DateTime.Now - this.lastFindFriends).TotalMilliseconds;
			if (ms < 60000)     // Call at most once a minute
			{
				int msi = (int)Math.Ceiling(60000 - ms);
				Timer Timer = null;

				Log.Informational("Delaying search " + msi.ToString() + " ms.");

				Timer = new Timer(async (P) =>
				{
					try
					{
						Timer?.Dispose();
						await this.FindFriends(MetaInfo);
					}
					catch (Exception ex)
					{
						Log.Exception(ex);
					}
				}, null, msi, Timeout.Infinite);

				return;
			}

			this.lastFindFriends = DateTime.Now;
			this.sensorJid = null;
			this.sensor = null;
			this.actuator = null;
			this.actuatorJid = null;

			foreach (RosterItem Item in this.xmppClient.Roster)
			{
				if (Item.IsInGroup("Sensor"))
				{
					this.sensorJid = Item.BareJid;
					this.sensor = this.GetReference(Item, "Sensor");
				}

				if (Item.IsInGroup("Actuator"))
				{
					this.actuatorJid = Item.BareJid;
					this.actuator = this.GetReference(Item, "Actuator");
				}
			}

			if (!string.IsNullOrEmpty(this.sensorJid))
				await this.SubscribeToSensorData();

			if (string.IsNullOrEmpty(this.sensorJid) || string.IsNullOrEmpty(this.actuatorJid))
			{
				List<SearchOperator> Search = new List<SearchOperator>();

				foreach (MetaDataTag Tag in MetaInfo)
				{
					if (Tag is MetaDataStringTag StringTag)
					{
						switch (StringTag.Name)
						{
							case "COUNTRY":
							case "REGION":
							case "CITY":
							case "AREA":
							case "STREET":
							case "STREETNR":
							case "BLD":
							case "APT":
							case "ROOM":
							case "NAME":
								Search.Add(new StringTagEqualTo(StringTag.Name, StringTag.StringValue));
								break;
						}
					}
				}

				Search.Add(new StringTagGreaterThan("TYPE", "MIoT "));

				Log.Informational("Searching for MIoT devices in my vicinity.");

				await this.registryClient.Search(0, 100, Search.ToArray(), (sender, e) =>
				{
					Log.Informational(e.Things.Length.ToString() + (e.More ? "+" : string.Empty) + " things found.");

					foreach (SearchResultThing Thing in e.Things)
					{
						foreach (MetaDataTag Tag in Thing.Tags)
						{
							if (Tag.Name == "TYPE" && Tag is MetaDataStringTag StringTag)
							{
								switch (Tag.StringValue)
								{
									case "MIoT Sensor":
										if (string.IsNullOrEmpty(this.sensorJid))
										{
											this.sensorJid = Thing.Jid;
											this.sensor = Thing.Node;

											RosterItem Item = this.xmppClient[this.sensorJid];
											if (Item != null)
											{
												this.xmppClient.UpdateRosterItem(this.sensorJid, Item.Name,
													this.AddReference(Item.Groups, "Sensor", Thing.Node));

												if (Item.State != SubscriptionState.Both && Item.State != SubscriptionState.To)
													this.xmppClient.RequestPresenceSubscription(this.sensorJid);
											}
											else
											{
												this.xmppClient.AddRosterItem(new RosterItem(this.sensorJid, string.Empty,
													this.AddReference(null, "Sensor", Thing.Node)));

												this.xmppClient.RequestPresenceSubscription(this.sensorJid);
											}
										}
										break;

									case "MIoT Actuator":
										if (string.IsNullOrEmpty(this.actuatorJid))
										{
											this.actuatorJid = Thing.Jid;
											this.actuator = Thing.Node;

											RosterItem Item = this.xmppClient[this.actuatorJid];
											if (Item != null)
											{
												this.xmppClient.UpdateRosterItem(this.actuatorJid, Item.Name, this.AddReference(Item.Groups, "Actuator", Thing.Node));

												if (Item.State != SubscriptionState.Both && Item.State != SubscriptionState.To)
													this.xmppClient.RequestPresenceSubscription(this.sensorJid);
											}
											else
												this.xmppClient.AddRosterItem(new RosterItem(this.actuatorJid, string.Empty, this.AddReference(null, "Actuator", Thing.Node)));
										}
										break;
								}
							}
						}
					}

					return Task.CompletedTask;

				}, null);
			}
		}

		private ThingReference GetReference(RosterItem Item, string Prefix)
		{
			string NodeId = string.Empty;
			string SourceId = string.Empty;
			string Partition = string.Empty;

			Prefix += ".";

			foreach (string Group in Item.Groups)
			{
				if (Group.StartsWith(Prefix))
				{
					string s = Group.Substring(Prefix.Length);
					int i = s.IndexOf(':');
					if (i < 0)
						continue;

					switch (s.Substring(0, i).ToLower())
					{
						case "nid":
							NodeId = s.Substring(i + 1);
							break;

						case "sid":
							SourceId = s.Substring(i + 1);
							break;

						case "prt":
							Partition = s.Substring(i + 1);
							break;
					}
				}
			}

			return new ThingReference(NodeId, SourceId, Partition);
		}

		private string[] AddReference(string[] Groups, string Prefix, IThingReference NodeReference)
		{
			List<string> Result = new List<string>()
			{
				Prefix
			};

			if (!string.IsNullOrEmpty(NodeReference.NodeId))
				Result.Add(Prefix + ".nid:" + NodeReference.NodeId);

			if (!string.IsNullOrEmpty(NodeReference.SourceId))
				Result.Add(Prefix + ".sid:" + NodeReference.SourceId);

			if (!string.IsNullOrEmpty(NodeReference.Partition))
				Result.Add(Prefix + ".prt:" + NodeReference.Partition);

			if (Groups != null)
			{
				foreach (string Group in Groups)
				{
					if (!Group.StartsWith(Prefix))
						Result.Add(Group);
				}
			}

			return Result.ToArray();
		}

		private Task XmppClient_OnRosterItemRemoved(object _, RosterItem Item)
		{
			Log.Informational("Roster item removed.", Item.BareJid);

			this.FriendshipLost(Item);

			return Task.CompletedTask;
		}

		private void FriendshipLost(RosterItem Item)
		{
			bool UpdateRegistration = false;

			if (string.Compare(Item.BareJid, this.sensorJid, true) == 0)
			{
				this.sensorJid = null;
				this.sensor = null;
				UpdateRegistration = true;
			}

			if (string.Compare(Item.BareJid, this.actuatorJid, true) == 0)
			{
				this.actuatorJid = null;
				this.actuator = null;
				UpdateRegistration = true;
			}

			if (UpdateRegistration)
				Task.Run(this.RegisterDevice);
		}

		private async Task XmppClient_OnRosterItemUpdated(object _, RosterItem Item)
		{
			bool IsSensor;

			Log.Informational("Roster item updated.", Item.BareJid);

			if (((IsSensor = (this.sensorJid != null && string.Compare(Item.BareJid, this.sensorJid, true) == 0)) ||
				(this.actuatorJid != null && string.Compare(Item.BareJid, this.actuatorJid, true) == 0)) &&
				(Item.State == SubscriptionState.None || Item.State == SubscriptionState.From) &&
				Item.PendingSubscription != PendingSubscription.Subscribe)
			{
				this.FriendshipLost(Item);
			}
			else if (IsSensor)
				await this.SubscribeToSensorData();
		}

		private Task XmppClient_OnRosterItemAdded(object _, RosterItem Item)
		{
			Log.Informational("Roster item added.", Item.BareJid);

			if (Item.IsInGroup("Sensor") || Item.IsInGroup("Actuator"))
			{
				Log.Informational("Requesting presence subscription.", Item.BareJid);
				this.xmppClient.RequestPresenceSubscription(Item.BareJid);
			}

			return Task.CompletedTask;
		}

		private async Task XmppClient_OnPresence(object Sender, PresenceEventArgs e)
		{
			Log.Informational("Presence received.", e.Availability.ToString(), e.From);

			if (this.sensorJid != null &&
				string.Compare(e.FromBareJID, this.sensorJid, true) == 0 &&
				e.IsOnline)
			{
				await this.SubscribeToSensorData();
			}
		}

		private async Task SubscribeToSensorData()
		{
			RosterItem SensorItem;

			if (!string.IsNullOrEmpty(this.sensorJid) &&
				(SensorItem = this.xmppClient[this.sensorJid]) != null)
			{
				if (SensorItem.HasLastPresence && SensorItem.LastPresence.IsOnline)
				{
					ThingReference[] Nodes;

					if (this.sensor.IsEmpty)
						Nodes = null;
					else
						Nodes = new ThingReference[] { this.sensor };

					if (this.subscription != null)
					{
						await this.subscription.Unsubscribe();
						this.subscription = null;
					}

					Log.Informational("Subscribing to events.", SensorItem.LastPresenceFullJid);

					this.subscription = await this.sensorClient.Subscribe(SensorItem.LastPresenceFullJid,
						Nodes, FieldType.Momentary, new FieldSubscriptionRule[]
						{
							new FieldSubscriptionRule("Light", this.light, 1),
							new FieldSubscriptionRule("Motion", this.motion.HasValue ?
								(double?)(this.motion.Value ? 1 : 0) : null, 1),
						},
						new Waher.Content.Duration(false, 0, 0, 0, 0, 0, 1),
						new Waher.Content.Duration(false, 0, 0, 0, 0, 1, 0), true);

					this.subscription.OnStateChanged += this.Subscription_OnStateChanged;
					this.subscription.OnFieldsReceived += this.Subscription_OnFieldsReceived;
					this.subscription.OnErrorsReceived += this.Subscription_OnErrorsReceived;
				}
				else if (SensorItem.State == SubscriptionState.From || SensorItem.State == SubscriptionState.None)
				{
					Log.Informational("Requesting presence subscription.", this.sensorJid);
					await this.xmppClient.RequestPresenceSubscription(this.sensorJid);
				}
			}
		}

		private Task Subscription_OnStateChanged(object _, SensorDataReadoutState NewState)
		{
			Log.Informational("Sensor subscription state changed.", NewState.ToString());
			return Task.CompletedTask;
		}

		private Task Subscription_OnFieldsReceived(object _, IEnumerable<Field> NewFields)
		{
			bool RecalcOutput = false;

			this.lastEventFields = DateTime.Now;

			foreach (Field Field in NewFields)
			{
				switch (Field.Name)
				{
					case "Light":
						if (Field is QuantityField Q)
						{
							MainPage.Instance.LightUpdated(Q.Value, Q.NrDecimals, Q.Unit);
							if (Q.Unit == "%")
							{
								this.light = Q.Value;
								RecalcOutput = true;
							}
						}
						break;

					case "Motion":
						if (Field is BooleanField B)
						{
							MainPage.Instance.MotionUpdated(B.Value);
							this.motion = B.Value;
							RecalcOutput = true;
						}
						break;
				}
			}

			if (RecalcOutput && this.motion.HasValue && this.light.HasValue)
			{
				bool Output = this.motion.Value && this.light.Value < 25;

				if (!string.IsNullOrEmpty(this.actuatorJid) &&
					(!this.output.HasValue || this.output.Value != Output))
				{
					RosterItem Actuator = this.xmppClient[this.actuatorJid];

					if (Actuator != null)
					{
						if (Actuator.HasLastPresence && Actuator.LastPresence.IsOnline)
						{
							ThingReference[] Nodes;

							if (this.actuator.IsEmpty)
								Nodes = null;
							else
								Nodes = new ThingReference[] { this.actuator };

							this.controlClient.Set(Actuator.LastPresenceFullJid, "Output", Output, Nodes);
							this.output = Output;
							this.lastOutput = DateTime.Now;

							MainPage.Instance.RelayUpdated(Output);
						}
						else if ((Actuator.State == SubscriptionState.From || Actuator.State == SubscriptionState.None) &&
							(DateTime.Now - this.lastRequestActuator).TotalHours >= 1.0)
						{
							this.xmppClient.RequestPresenceSubscription(this.actuatorJid);
							this.lastRequestActuator = DateTime.Now;
						}
					}
				}
			}

			return Task.CompletedTask;
		}

		private Task Subscription_OnErrorsReceived(object _, IEnumerable<ThingError> NewErrors)
		{
			this.lastEventErrors = DateTime.Now;

			foreach (ThingError Error in NewErrors)
				Log.Error(Error.ErrorMessage);

			return Task.CompletedTask;
		}

		private async void SecondTimerCallback(object State)
		{
			try
			{
				DateTime Now = DateTime.Now;
				double SecondsSinceLastEvent = Math.Min(
					(Now - this.lastEventFields).TotalSeconds,
					(Now - this.lastEventErrors).TotalSeconds);
				double SecondsSinceLastOutput = (Now - this.lastOutput).TotalSeconds;
				RosterItem Item;
				bool Search = false;

				if (this.subscription != null && SecondsSinceLastEvent > 70)
					await this.SubscribeToSensorData();
				else if (SecondsSinceLastEvent > 60 * 60 * 24)
				{
					if (!string.IsNullOrEmpty(this.sensorJid))
					{
						Item = this.xmppClient[this.sensorJid];

						this.sensor = null;
						this.sensorJid = null;

						if (Item != null)
							await this.xmppClient.UpdateRosterItem(this.sensorJid, Item.Name, this.RemoveReference(Item.Groups, "Sensor"));
					}

					Search = true;
				}

				if (SecondsSinceLastOutput > 60 * 60 * 24)
				{
					if (!string.IsNullOrEmpty(this.actuatorJid))
					{
						Item = this.xmppClient[this.actuatorJid];

						this.actuator = null;
						this.actuatorJid = null;

						if (Item != null)
							await this.xmppClient.UpdateRosterItem(this.actuatorJid, Item.Name, this.RemoveReference(Item.Groups, "Actuator"));
					}

					Search = true;
				}

				if (Search)
					await this.RegisterDevice();
			}
			catch (Exception ex)
			{
				Log.Exception(ex);
			}
		}

		private string[] RemoveReference(string[] Groups, string Prefix)
		{
			List<string> Result = new List<string>();

			if (Groups != null)
			{
				foreach (string Group in Groups)
				{
					if (!Group.StartsWith(Prefix))
						Result.Add(Group);
				}
			}

			return Result.ToArray();
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

			this.subscription?.Unsubscribe();
			this.subscription = null;

			this.registryClient?.Dispose();
			this.registryClient = null;

			this.chatServer?.Dispose();
			this.chatServer = null;

			this.bobClient?.Dispose();
			this.bobClient = null;

			this.sensorServer?.Dispose();
			this.sensorServer = null;

			this.sensorClient?.Dispose();
			this.sensorClient = null;

			this.controlClient?.Dispose();
			this.controlClient = null;

			this.xmppClient?.DisposeAsync().Wait();
			this.xmppClient = null;

			this.secondTimer?.Dispose();
			this.secondTimer = null;

			this.db?.Stop()?.Wait();
			this.db?.Flush()?.Wait();

			Log.TerminateAsync().Wait();

			deferral.Complete();
		}
	}
}
