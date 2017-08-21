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
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

using Windows.Devices.Enumeration;
using Windows.Storage;
using Microsoft.Maker.RemoteWiring;
using Microsoft.Maker.Serial;
using SkiaSharp;
using Waher.Content;
using Waher.Content.Images;
using Waher.Content.Markdown;
using Waher.Content.Markdown.Web;
using Waher.Events;
using Waher.Networking;
using Waher.Networking.HTTP;
using Waher.Networking.Sniffers;
using Waher.Persistence;
using Waher.Persistence.Files;
using Waher.Persistence.Filters;
using Waher.Runtime.Inventory;
using Waher.Runtime.Settings;
using Waher.Script;
using Waher.Script.Graphs;
using Waher.Security;
using Waher.Security.JWT;

using SensorHttp.History;

namespace SensorHttp
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
		private HttpServer httpServer = null;
		private IUserSource users = new Users();
		private JwtFactory tokenFactory = new JwtFactory();
		private JwtAuthentication tokenAuthentication;

		private const int windowSize = 10;
		private const int spikePos = windowSize / 2;
		private int?[] windowA0 = new int?[windowSize];
		private int nrA0 = 0;
		private int sumA0 = 0;

		private int? lastMinute = null;
		private double? minLight = null;
		private double? maxLight = null;
		private double sumLight = 0;
		private int sumMovement = 0;
		private int nrTerms = 0;
		private DateTime minLightAt = DateTime.MinValue;
		private DateTime maxLightAt = DateTime.MinValue;
		private string deviceId;
		private double? lastLight = null;
		private bool? lastMovement = null;

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
				Window.Current.Activate();
				Task.Run((Action)this.Init);
			}
		}

		private async void Init()
		{
			try
			{
				Log.Informational("Starting application.");
				instance = this;

				Types.Initialize(
					typeof(FilesProvider).GetTypeInfo().Assembly,
					typeof(RuntimeSettings).GetTypeInfo().Assembly,
					typeof(IContentEncoder).GetTypeInfo().Assembly,
					typeof(ImageCodec).GetTypeInfo().Assembly,
					typeof(MarkdownDocument).GetTypeInfo().Assembly,
					typeof(MarkdownToHtmlConverter).GetTypeInfo().Assembly,
					typeof(Expression).GetTypeInfo().Assembly,
					typeof(Graph).GetTypeInfo().Assembly,
					typeof(App).GetTypeInfo().Assembly);

				Database.Register(new FilesProvider(ApplicationData.Current.LocalFolder.Path +
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
							MainPage.Instance.DigitalPinUpdated(0, this.arduino.digitalRead(0));

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
								this.lastMovement = (value == PinState.HIGH);
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
						switch (req.Header.Accept.GetBestContentType("text/xml", "application/xml", "application/json", "image/png", "image/jpeg", "image/webp"))
						{
							case "text/xml":
							case "application/xml":
								this.ReturnMomentaryAsXml(req, resp);
								break;

							case "application/json":
								this.ReturnMomentaryAsJson(req, resp);
								break;

							case "image/png":
								this.ReturnMomentaryAsPng(req, resp);
								break;

							case "image/jpg":
								this.ReturnMomentaryAsJpg(req, resp);
								break;

							case "image/webp":
								this.ReturnMomentaryAsWebp(req, resp);
								break;

							default:
								throw new NotAcceptableException();
						}
					}
					else
						this.ReturnMomentaryAsXml(req, resp);
				}, this.tokenAuthentication);

				this.httpServer.Register("/MomentaryPng", (req, resp) =>
				{
					IUser User;

					if (!req.Session.TryGetVariable("User", out Variable v) ||
						(User = v.ValueObject as IUser) == null)
					{
						throw new ForbiddenException();
					}

					resp.SetHeader("Cache-Control", "max-age=0, no-cache, no-store");
					this.ReturnMomentaryAsPng(req, resp);
				}, true, false, true);

				/*
				this.httpServer.Register("/History", async (req, resp) =>
				{
					try
					{
						if (req.Header.Accept != null)
						{
							switch (req.Header.Accept.GetBestContentType("text/xml", "application/xml", "application/json", "image/png"))
							{
								case "text/xml":
								case "application/xml":
									this.ReturnMomentaryAsXml(req, resp);
									break;

								case "application/json":
									this.ReturnMomentaryAsJson(req, resp);
									break;

								case "image/png":
									this.ReturnMomentaryAsPng(req, resp);
									break;

								default:
									throw new NotAcceptableException();
							}
						}
						else
							this.ReturnMomentaryAsXml(req, resp);

						resp.SendResponse();
					}
					catch (Exception ex)
					{
						resp.SendResponse(ex);
					}
				}, this.tokenAuthentication);
				*/

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

		private async void SampleValues(object State)
		{
			try
			{
				DateTime Timestamp = DateTime.Now;
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
					MainPage.Instance.LightUpdated(Light, 2, "%");

					this.sumLight += Light;
					this.sumMovement += (D0 == PinState.HIGH ? 1 : 0);
					this.nrTerms++;

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
							Movement = D0,
							MinLight = this.minLight,
							MinLightAt = this.minLightAt,
							MaxLight = this.maxLight,
							MaxLightAt = this.maxLightAt,
							AvgLight = (this.nrTerms == 0 ? (double?)null : this.sumLight / this.nrTerms),
							AvgMovement = (this.nrTerms == 0 ? (double?)null : (this.sumMovement * 100.0) / this.nrTerms)
						};

						await Database.Insert(Rec);

						this.minLight = null;
						this.minLightAt = DateTime.MinValue;
						this.maxLight = null;
						this.maxLightAt = DateTime.MinValue;
						this.sumLight = 0;
						this.sumMovement = 0;
						this.nrTerms = 0;

						foreach (LastMinute Rec2 in await Database.Find<LastMinute>(new FilterFieldLesserThan("Timestamp", Timestamp.AddMinutes(-100))))
							await Database.Delete(Rec2);

						if (Timestamp.Minute == 0)
						{
							DateTime From = new DateTime(Timestamp.Year, Timestamp.Month, Timestamp.Day, Timestamp.Hour, 0, 0).AddHours(-1);
							DateTime To = From.AddHours(1);
							int NLight = 0;
							int NMovement = 0;

							LastHour HourRec = new LastHour()
							{
								Timestamp = Timestamp,
								Light = Light,
								Movement = D0,
								MinLight = Rec.MinLight,
								MinLightAt = Rec.MinLightAt,
								MaxLight = Rec.MaxLight,
								MaxLightAt = Rec.MaxLightAt,
								AvgLight = 0,
								AvgMovement = 0
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

								if (Rec2.AvgMovement.HasValue)
								{
									HourRec.AvgMovement += Rec2.AvgMovement.Value;
									NMovement++;
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

							if (NMovement == 0)
								HourRec.AvgMovement = null;
							else
								HourRec.AvgMovement /= NMovement;

							await Database.Insert(HourRec);

							foreach (LastHour Rec2 in await Database.Find<LastHour>(new FilterFieldLesserThan("Timestamp", Timestamp.AddHours(-100))))
								await Database.Delete(Rec2);

							if (Timestamp.Hour == 0)
							{
								From = new DateTime(Timestamp.Year, Timestamp.Month, Timestamp.Day, 0, 0, 0).AddDays(-1);
								To = From.AddDays(1);
								NLight = 0;
								NMovement = 0;

								LastDay DayRec = new LastDay()
								{
									Timestamp = Timestamp,
									Light = Light,
									Movement = D0,
									MinLight = HourRec.MinLight,
									MinLightAt = HourRec.MinLightAt,
									MaxLight = HourRec.MaxLight,
									MaxLightAt = HourRec.MaxLightAt,
									AvgLight = 0,
									AvgMovement = 0
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

									if (Rec2.AvgMovement.HasValue)
									{
										DayRec.AvgMovement += Rec2.AvgMovement.Value;
										NMovement++;
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

								if (NMovement == 0)
									DayRec.AvgMovement = null;
								else
									DayRec.AvgMovement /= NMovement;

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

		internal static string ToString(double Value, int NrDec)
		{
			return Value.ToString("F" + NrDec.ToString()).Replace(NumberFormatInfo.CurrentInfo.NumberDecimalSeparator, ".");
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

			if (this.lastLight.HasValue)
			{
				Response.Write("<Light value='");
				Response.Write(ToString(this.lastLight.Value, 2));
				Response.Write("' unit='%'/>");
			}

			if (this.lastMovement.HasValue)
			{
				Response.Write("<Movement value='");
				Response.Write(this.lastMovement.Value ? "true" : "false");
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

			if (this.lastLight.HasValue)
			{
				Response.Write(",\"light\":{\"value\":");
				Response.Write(ToString(this.lastLight.Value, 2));
				Response.Write(",\"unit\":\"%\"}");
			}

			if (this.lastMovement.HasValue)
			{
				Response.Write(",\"movement\":");
				Response.Write(this.lastMovement.Value ? "true" : "false");
			}

			Response.Write('}');
		}

		private void ReturnMomentaryAsPng(HttpRequest Request, HttpResponse Response)
		{
			Response.Return(this.GenerateGauge(Request.Header));
		}

		private void ReturnMomentaryAsJpg(HttpRequest Request, HttpResponse Response)
		{
			SKImage Gauge = this.GenerateGauge(Request.Header);
			SKData Data = Gauge.Encode(SKEncodedImageFormat.Jpeg, 90);
			byte[] Binary = Data.ToArray();

			Response.ContentType = "image/jpeg";
			Response.Write(Binary);
		}

		private void ReturnMomentaryAsWebp(HttpRequest Request, HttpResponse Response)
		{
			SKImage Gauge = this.GenerateGauge(Request.Header);
			SKData Data = Gauge.Encode(SKEncodedImageFormat.Webp, 90);
			byte[] Binary = Data.ToArray();

			Response.ContentType = "image/webp";
			Response.Write(Binary);
		}

		private SKImage GenerateGauge(HttpRequestHeader Header)
		{
			if (!Header.TryGetQueryParameter("Width", out string s) || !int.TryParse(s, out int Width))
				Width = 480;
			else if (Width <= 0)
				throw new BadRequestException();

			if (!Header.TryGetQueryParameter("Height", out s) || !int.TryParse(s, out int Height))
				Height = 300;
			else if (Width <= 0)
				throw new BadRequestException();

			using (SKSurface Surface = SKSurface.Create(Width, Height, SKImageInfo.PlatformColorType, SKAlphaType.Premul))
			{
				SKCanvas Canvas = Surface.Canvas;
				Canvas.Clear(SKColors.White);

				float NeedleX0 = Width * 0.5f;
				float NeedleY0 = Height * 0.9f;
				float OuterRadius = (float)Math.Min(Height * 0.8, Width * 0.4);
				float LabelRadius = OuterRadius * 1.01f;
				float InnerRadius = OuterRadius * 0.6f;
				float NeedleRadius = OuterRadius * 0.95f;
				float NutRadius = OuterRadius * 0.05f;
				SKRect Rect;
				SKPath Path = new SKPath();
				SKShader Gradient = SKShader.CreateSweepGradient(new SKPoint(NeedleX0, NeedleY0),
					new SKColor[] { (lastMovement.HasValue && lastMovement.Value ? SKColors.Green : SKColors.Black), SKColors.White },
					new float[] { 0, 1 });
				SKPaint GaugeBackground = new SKPaint()
				{
					IsAntialias = true,
					Style = SKPaintStyle.Fill,
					Shader = Gradient
				};
				SKPaint GaugeOutline = new SKPaint()
				{
					IsAntialias = true,
					Style = SKPaintStyle.Stroke,
					Color = SKColors.Black
				};

				Rect = new SKRect(NeedleX0 - OuterRadius, NeedleY0 - OuterRadius, NeedleX0 + OuterRadius, NeedleY0 + OuterRadius);
				Path.ArcTo(Rect, -180, 180, true);

				Rect = new SKRect(NeedleX0 - InnerRadius, NeedleY0 - InnerRadius, NeedleX0 + InnerRadius, NeedleY0 + InnerRadius);
				Path.ArcTo(Rect, 0, -180, false);
				Path.Close();

				Canvas.DrawPath(Path, GaugeBackground);
				Canvas.DrawPath(Path, GaugeOutline);

				GaugeBackground.Dispose();
				GaugeOutline.Dispose();
				Gradient.Dispose();
				Path.Dispose();

				SKPaint Font = new SKPaint()
				{
					IsAntialias = true,
					Color = SKColors.Black,
					HintingLevel = SKPaintHinting.Full,
					TextSize = Height * 0.05f
				};

				SKPaint Needle = new SKPaint()
				{
					IsAntialias = true,
					Color = SKColors.Black,
					Style = SKPaintStyle.Fill
				};

				Font.GetFontMetrics(out SKFontMetrics FontMetrics);
				float TextHeight = FontMetrics.Descent - FontMetrics.Ascent;
				float TextWidth;

				for (int i = 0; i <= 100; i += 10)
				{
					s = i.ToString() + "%";
					TextWidth = Font.MeasureText(s);

					float LabelDeg = -90 + i * 1.8f;
					float LabelRad = (float)(LabelDeg * Math.PI / 180);
					float LabelX = (float)(LabelRadius * Math.Sin(LabelRad) + NeedleX0);
					float LabelY = (float)(NeedleY0 - LabelRadius * Math.Cos(LabelRad));
					float OuterX = (float)(OuterRadius * Math.Sin(LabelRad) + NeedleX0);
					float OuterY = (float)(NeedleY0 - OuterRadius * Math.Cos(LabelRad));
					float NeedleX1 = (float)(NeedleRadius * Math.Sin(LabelRad) + NeedleX0);
					float NeedleY1 = (float)(NeedleY0 - NeedleRadius * Math.Cos(LabelRad));

					Canvas.DrawLine(OuterX, OuterY, NeedleX1, NeedleY1, Needle);

					Canvas.Translate(LabelX, LabelY);
					Canvas.RotateDegrees(LabelDeg);
					Canvas.Translate(-TextWidth * 0.5f, -TextHeight * 0.5f);
					Canvas.DrawText(s, 0, 0, Font);
					Canvas.ResetMatrix();
				}

				if (this.lastLight.HasValue)
				{
					float AngleDeg = (float)(this.lastLight.Value - 50) * 90.0f / 50;
					double AngleRad = AngleDeg * Math.PI / 180;
					float NeedleX1 = (float)(NeedleRadius * Math.Sin(AngleRad) + NeedleX0);
					float NeedleY1 = (float)(NeedleY0 - NeedleRadius * Math.Cos(AngleRad));

					Path = new SKPath();
					Rect = new SKRect(NeedleX0 - NutRadius, NeedleY0 - NutRadius, NeedleX0 + NutRadius, NeedleY0 + NutRadius);
					Path.ArcTo(Rect, AngleDeg - 180, -180, true);

					Path.LineTo(NeedleX1, NeedleY1);
					Path.Close();

					Canvas.DrawPath(Path, Needle);

					Path.Dispose();
					Needle.Dispose();
				}

				return Surface.Snapshot();
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

		public static string Light
		{
			get
			{
				if (instance.lastLight.HasValue)
					return ToString(instance.lastLight.Value, 2) + "%";
				else
					return string.Empty;
			}
		}

		public static string Movement
		{
			get
			{
				if (instance.lastMovement.HasValue)
					return instance.lastMovement.Value ? "Detected" : "Not detected";
				else
					return string.Empty;
			}
		}

		public static LastMinute[] GetLastMinutes()
		{
			Task<LastMinute[]> T = GetLastMinutesAsync();
			T.Wait();
			return T.Result;
		}

		public static async Task<LastMinute[]> GetLastMinutesAsync()
		{
			List<LastMinute> Result = new List<LastMinute>();

			foreach (LastMinute Rec in await Database.Find<LastMinute>("Timestamp"))
				Result.Add(Rec);

			return Result.ToArray();
		}

		public static LastHour[] GetLastHours()
		{
			Task<LastHour[]> T = GetLastHoursAsync();
			T.Wait();
			return T.Result;
		}

		public static async Task<LastHour[]> GetLastHoursAsync()
		{
			List<LastHour> Result = new List<LastHour>();

			foreach (LastHour Rec in await Database.Find<LastHour>("Timestamp"))
				Result.Add(Rec);

			return Result.ToArray();
		}

		public static LastDay[] GetLastDays()
		{
			Task<LastDay[]> T = GetLastDaysAsync();
			T.Wait();
			return T.Result;
		}

		public static async Task<LastDay[]> GetLastDaysAsync()
		{
			List<LastDay> Result = new List<LastDay>();

			foreach (LastDay Rec in await Database.Find<LastDay>("Timestamp"))
				Result.Add(Rec);

			return Result.ToArray();
		}
	}
}
