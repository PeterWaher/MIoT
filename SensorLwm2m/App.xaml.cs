using System;
using System.Collections.Generic;
using System.Globalization;
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
using Waher.Content;
using Waher.Events;
using Waher.Networking.CoAP;
using Waher.Networking.CoAP.ContentFormats;
using Waher.Networking.CoAP.Options;
using Waher.Networking.LWM2M;
using Waher.Persistence;
using Waher.Persistence.Files;
using Waher.Persistence.Filters;
using Waher.Runtime.Inventory;
using Waher.Runtime.Settings;
using Waher.Security;
using Waher.Security.DTLS;

using SensorLwm2m.History;
using SensorLwm2m.IPSO;

namespace SensorLwm2m
{
	/// <summary>
	/// Provides application-specific behavior to supplement the default Application class.
	/// </summary>
	sealed partial class App : Application
	{
		private static App instance = null;
		private UsbSerial arduinoUsb = null;
		private RemoteDevice arduino = null;
		private Timer sampleTimer = null;
		private CoapEndpoint coapEndpoint = null;
		private IUserSource users = new Users();
		private Lwm2mClient lwm2mClient = null;

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
		private string deviceId;
		private double? lastLight = null;
		private bool? lastMotion = null;
		private CoapResource lightResource = null;
		private CoapResource motionResource = null;
		private CoapResource momentaryResource = null;
		private DigitalInputInstance digitalInput0;
		private AnalogInputInstance analogInput0;
		private GenericSensorInstance genericSensor0;
		private GenericSensorInstance genericSensor1;
		private IlluminanceSensorInstance illuminanceSensor0;
		private PresenceSensorInstance presenceSensor0;
		private PercentageSensorInstance percentageSensor0;

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
					typeof(IDtlsCredentials).GetTypeInfo().Assembly,
					typeof(Lwm2mClient).GetTypeInfo().Assembly,
					typeof(App).GetTypeInfo().Assembly);

				Database.Register(new FilesProvider(Windows.Storage.ApplicationData.Current.LocalFolder.Path +
					Path.DirectorySeparatorChar + "Data", "Default", 8192, 1000, 8192, Encoding.UTF8, 10000));

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
						this.arduino.DeviceReady += () =>
						{
							Log.Informational("Device ready.");

							this.arduino.pinMode(13, PinMode.OUTPUT);    // Onboard LED.
							this.arduino.digitalWrite(13, PinState.HIGH);

							this.arduino.pinMode(0, PinMode.INPUT);      // PIR sensor (motion detection).
							PinState Pin0 = this.arduino.digitalRead(0);
							this.lastMotion = Pin0 == PinState.HIGH;
							MainPage.Instance.DigitalPinUpdated(0, Pin0);

							this.arduino.pinMode(1, PinMode.OUTPUT);     // Relay.
							this.arduino.digitalWrite(1, 0);             // Relay set to 0

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

							if (pin == 0)
							{
								bool Input = (value == PinState.HIGH);
								this.lastMotion = Input;
								this.digitalInput0?.Set(Input);
								this.presenceSensor0?.Set(Input);
								this.genericSensor0?.Set(Input ? 1.0 : 0.0);
								this.motionResource?.TriggerAll();
								this.momentaryResource?.TriggerAll();
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

				//this.coapEndpoint = new CoapEndpoint(new int[] { CoapEndpoint.DefaultCoapPort },
				//	new int[] { CoapEndpoint.DefaultCoapsPort }, this.users, string.Empty, false, false);

				this.coapEndpoint = new CoapEndpoint(new int[] { 5783 }, new int[] { 5784 }, null, null,
					false, false);

				this.lightResource = this.coapEndpoint.Register("/Light", (req, resp) =>
				{
					string s;

					if (this.lastLight.HasValue)
						s = ToString(this.lastLight.Value, 2) + " %";
					else
						s = "-";

					resp.Respond(CoapCode.Content, s, 64);

				}, Notifications.Unacknowledged, "Light, in %.", null, null,
					new int[] { PlainText.ContentFormatCode });

				this.lightResource?.TriggerAll(new TimeSpan(0, 0, 5));

				this.motionResource = this.coapEndpoint.Register("/Motion", (req, resp) =>
				{
					string s;

					if (this.lastMotion.HasValue)
						s = this.lastMotion.Value ? "true" : "false";
					else
						s = "-";

					resp.Respond(CoapCode.Content, s, 64);

				}, Notifications.Acknowledged, "Motion detector.", null, null,
					new int[] { PlainText.ContentFormatCode });

				this.motionResource?.TriggerAll(new TimeSpan(0, 1, 0));

				this.momentaryResource = this.coapEndpoint.Register("/Momentary", (req, resp) =>
				{
					if (req.IsAcceptable(Xml.ContentFormatCode))
						this.ReturnMomentaryAsXml(req, resp);
					else if (req.IsAcceptable(Json.ContentFormatCode))
						this.ReturnMomentaryAsJson(req, resp);
					else if (req.IsAcceptable(PlainText.ContentFormatCode))
						this.ReturnMomentaryAsPlainText(req, resp);
					else if (req.Accept.HasValue)
						throw new CoapException(CoapCode.NotAcceptable);
					else
						this.ReturnMomentaryAsPlainText(req, resp);

				}, Notifications.Acknowledged, "Momentary values.", null, null,
					new int[] { Xml.ContentFormatCode, Json.ContentFormatCode, PlainText.ContentFormatCode });

				this.momentaryResource?.TriggerAll(new TimeSpan(0, 0, 5));

				this.lwm2mClient = new Lwm2mClient("MIoT:Sensor:" + this.deviceId, this.coapEndpoint,
					new Lwm2mSecurityObject(),
					new Lwm2mServerObject(),
					new Lwm2mAccessControlObject(),
					new Lwm2mDeviceObject("Waher Data AB", "SensorLwm2m", this.deviceId, "1.0", "Sensor", "1.0", "1.0"),
					new DigitalInput(this.digitalInput0 = new DigitalInputInstance(0, this.lastMotion, "Motion Detector", "PIR")),
					new AnalogInput(this.analogInput0 = new AnalogInputInstance(0, this.lastLight, 0, 100, "Ambient Light Sensor", "%")),
					new GenericSensor(this.genericSensor0 = new GenericSensorInstance(0, null, string.Empty, 0, 1, "Motion Detector", "PIR"),
						this.genericSensor1 = new GenericSensorInstance(1, this.lastLight, "%", 0, 100, "Ambient Light Sensor", "%")),
					new IlluminanceSensor(this.illuminanceSensor0 = new IlluminanceSensorInstance(0, this.lastLight, "%", 0, 100)),
					new PresenceSensor(this.presenceSensor0 = new PresenceSensorInstance(0, this.lastMotion, "PIR")),
					new PercentageSensor(this.percentageSensor0 = new PercentageSensorInstance(0, this.lastLight, 0, 100, "Ambient Light Sensor")));

				await this.lwm2mClient.LoadBootstrapInfo();

				this.lwm2mClient.OnStateChanged += (sender, e) =>
				{
					Log.Informational("LWM2M state changed to " + this.lwm2mClient.State.ToString() + ".");
				};

				this.lwm2mClient.OnBootstrapCompleted += (sender, e) =>
				{
					Log.Informational("Bootstrap procedure completed.");
				};

				this.lwm2mClient.OnBootstrapFailed += (sender, e) =>
				{
					Log.Error("Bootstrap procedure failed.");

					this.coapEndpoint.ScheduleEvent(async (P) =>
					{
						try
						{
							await this.RequestBootstrap();
						}
						catch (Exception ex)
						{
							Log.Critical(ex);
						}
					}, DateTime.Now.AddMinutes(15), null);
				};

				this.lwm2mClient.OnRegistrationSuccessful += (sender, e) =>
				{
					Log.Informational("Server registration completed.");
				};

				this.lwm2mClient.OnRegistrationFailed += (sender, e) =>
				{
					Log.Error("Server registration failed.");
				};

				this.lwm2mClient.OnDeregistrationSuccessful += (sender, e) =>
				{
					Log.Informational("Server deregistration completed.");
				};

				this.lwm2mClient.OnDeregistrationFailed += (sender, e) =>
				{
					Log.Error("Server deregistration failed.");
				};

				this.lwm2mClient.OnRebootRequest += async (sender, e) =>
				{
					Log.Warning("Reboot is requested.");

					try
					{
						await this.RequestBootstrap();
					}
					catch (Exception ex)
					{
						Log.Critical(ex);
					}
				};

				await this.RequestBootstrap();
			}
			catch (Exception ex)
			{
				Log.Emergency(ex);

				MessageDialog Dialog = new MessageDialog(ex.Message, "Error");
				await MainPage.Instance.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
					async () => await Dialog.ShowAsync());
			}
		}

		private async Task RequestBootstrap()
		{
			//if (!await this.lwm2mClient.RequestBootstrap())   Due to an error in the Leshan bootstrap server hosted at eclipse.org, 
			//                                                  the bootstrap information provided will be erroneous.

			await this.lwm2mClient.RequestBootstrap(new Lwm2mServerReference("leshan.eclipse.org", 5783));

			/* If you're not using a bootstrap server, you need to register your client with the LWM2M servers yourself.
			 * This can be done as follows:
			 * 
			 *    this.lwm2mClient.Register(60, new Lwm2mServerReference("leshan.eclipse.org"));
			 * 
			 * Make sure to update the registration before the lifetime of the registration (60) elapses:
			 * 
			 *    this.lwm2mClient.RegisterUpdate();
			 */
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

		internal static string ToString(double Value, int NrDec)
		{
			return Value.ToString("F" + NrDec.ToString()).Replace(NumberFormatInfo.CurrentInfo.NumberDecimalSeparator, ".");
		}

		private void ReturnMomentaryAsXml(CoapMessage Request, CoapResponse Response)
		{
			StringBuilder s = new StringBuilder();

			s.Append("<m ts='");
			s.Append(DateTime.Now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
			s.Append("'>");

			if (this.lastLight.HasValue)
			{
				s.Append("<l u='%'>");
				s.Append(ToString(this.lastLight.Value, 2));
				s.Append("</l>");
			}

			if (this.lastMotion.HasValue)
			{
				s.Append("<md>");
				s.Append(this.lastMotion.Value ? "true" : "false");
				s.Append("</md>");
			}

			s.Append("</m>");

			Response.Respond(CoapCode.Content, s.ToString(), 64, new CoapOptionContentFormat(Xml.ContentFormatCode));
		}

		private void ReturnMomentaryAsJson(CoapMessage Request, CoapResponse Response)
		{
			StringBuilder s = new StringBuilder();

			s.Append("{\"ts\":\"");
			s.Append(DateTime.Now.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ"));
			s.Append('"');

			if (this.lastLight.HasValue)
			{
				s.Append(",\"l\":{\"v\":");
				s.Append(ToString(this.lastLight.Value, 2));
				s.Append(",\"u\":\"%\"}");
			}

			if (this.lastMotion.HasValue)
			{
				s.Append(",\"md\":");
				s.Append(this.lastMotion.Value ? "true" : "false");
			}

			s.Append('}');

			Response.Respond(CoapCode.Content, s.ToString(), 64, new CoapOptionContentFormat(Json.ContentFormatCode));
		}

		private void ReturnMomentaryAsPlainText(CoapMessage Request, CoapResponse Response)
		{
			StringBuilder s = new StringBuilder();

			s.Append("Timestamp: ");
			s.AppendLine(DateTime.Now.ToUniversalTime().ToString());

			if (this.lastLight.HasValue)
			{
				s.Append("Light: ");
				s.Append(ToString(this.lastLight.Value, 2));
				s.AppendLine(" %");
			}

			if (this.lastMotion.HasValue)
			{
				s.Append("Motion detected: ");
				s.AppendLine(this.lastMotion.Value ? "true" : "false");
			}

			Response.Respond(CoapCode.Content, s.ToString(), 64);
		}

		private async void SampleValues(object State)
		{
			try
			{
				ushort A0 = this.arduino.analogRead("A0");
				PinState D0 = this.arduino.digitalRead(0);

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
					this.lastLight = Light;
					this.analogInput0?.Set(Light);
					this.genericSensor1?.Set(Light);
					this.illuminanceSensor0?.Set(Light);
					this.percentageSensor0?.Set(Light);
					MainPage.Instance.LightUpdated(Light, 2, "%");

					this.sumLight += Light;
					this.sumMotion += (D0 == PinState.HIGH ? 1 : 0);
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
							Motion = D0,
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
								Motion = D0,
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
									Motion = D0,
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

			if (instance == this)
				instance = null;

			if (this.sampleTimer != null)
			{
				this.sampleTimer.Dispose();
				this.sampleTimer = null;
			}

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

			Log.Terminate();

			deferral.Complete();
		}

	}
}
