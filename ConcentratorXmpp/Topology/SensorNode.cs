using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Waher.Runtime.Language;
using Waher.Events;
using Waher.Persistence;
using Waher.Persistence.Filters;
using Waher.Things;
using Waher.Things.DisplayableParameters;
using Waher.Things.SensorData;
using ConcentratorXmpp.History;

namespace ConcentratorXmpp.Topology
{
	public class SensorNode : ThingReference, ISensor
	{
		public const string NodeID = "Sensor";

		public SensorNode()
			: base(NodeID, MeteringTopology.ID, string.Empty)
		{
		}

		public string LocalId => this.NodeId;
		public string LogId => this.NodeId;
		public bool HasChildren => false;
		public bool ChildrenOrdered => false;
		public bool IsReadable => true;
		public bool IsControllable => false;
		public bool HasCommands => false;
		public IThingReference Parent => null;
		public DateTime LastChanged => DateTime.MinValue;
		public NodeState State => NodeState.None;   // TODO
		public Task<IEnumerable<INode>> ChildNodes => null;
		public Task<IEnumerable<ICommand>> Commands => null;

		public Task<bool> AcceptsChildAsync(INode Child)
		{
			return Task.FromResult<bool>(false);
		}

		public Task<bool> AcceptsParentAsync(INode Parent)
		{
			return Task.FromResult<bool>(false);
		}

		public Task AddAsync(INode Child)
		{
			throw new NotSupportedException();
		}

		public Task<bool> CanAddAsync(RequestOrigin Caller)
		{
			return Task.FromResult<bool>(false);
		}

		public Task<bool> CanDestroyAsync(RequestOrigin Caller)
		{
			return Task.FromResult<bool>(false);
		}

		public Task<bool> CanEditAsync(RequestOrigin Caller)
		{
			return Task.FromResult<bool>(false);
		}

		public Task<bool> CanViewAsync(RequestOrigin Caller)
		{
			return Task.FromResult<bool>(true);
		}

		public Task DestroyAsync()
		{
			throw new NotSupportedException();
		}

		public async Task<IEnumerable<Parameter>> GetDisplayableParametersAsync(Language Language, RequestOrigin Caller)
		{
			LinkedList<Parameter> Parameters = new LinkedList<Parameter>();

			if (App.Instance.Light.HasValue)
				Parameters.AddLast(new DoubleParameter("Light", await Language.GetStringAsync(typeof(MeteringTopology), 2, "Light (%)"), App.Instance.Light.Value));

			if (App.Instance.Motion.HasValue)
				Parameters.AddLast(new BooleanParameter("Motion", await Language.GetStringAsync(typeof(MeteringTopology), 3, "Motion"), App.Instance.Motion.Value));

			return Parameters;
		}

		public Task<IEnumerable<Message>> GetMessagesAsync(RequestOrigin Caller)
		{
			return Task.FromResult<IEnumerable<Message>>(null);
		}

		public Task<string> GetTypeNameAsync(Language Language)
		{
			return Language.GetStringAsync(typeof(MeteringTopology), 4, "Sensor Node");
		}

		public Task<bool> MoveDownAsync(RequestOrigin Caller)
		{
			return Task.FromResult<bool>(false);
		}

		public Task<bool> MoveUpAsync(RequestOrigin Caller)
		{
			return Task.FromResult<bool>(false);
		}

		public Task<bool> RemoveAsync(INode Child)
		{
			return Task.FromResult<bool>(false);
		}

		public async void StartReadout(ISensorReadout Request)
		{
			try
			{
				Log.Informational("Performing readout.", this.LogId, Request.Actor);

				List<Field> Fields = new List<Field>();
				DateTime Now = DateTime.Now;

				if (Request.IsIncluded(FieldType.Identity))
					Fields.Add(new StringField(this, Now, "Device ID", App.Instance.DeviceId, FieldType.Identity, FieldQoS.AutomaticReadout));

				if (App.Instance.Light.HasValue)
				{
					Fields.Add(new QuantityField(this, Now, "Light", App.Instance.Light.Value, 2, "%",
						FieldType.Momentary, FieldQoS.AutomaticReadout));
				}

				if (App.Instance.Motion.HasValue)
				{
					Fields.Add(new BooleanField(this, Now, "Motion", App.Instance.Motion.Value,
						FieldType.Momentary, FieldQoS.AutomaticReadout));
				}

				if (Request.IsIncluded(FieldType.Historical))
				{
					Request.ReportFields(false, Fields);      // Allows for immediate response of momentary values.
					Fields.Clear();

					foreach (LastMinute Rec in await Database.Find<LastMinute>(new FilterAnd(
						new FilterFieldGreaterOrEqualTo("Timestamp", Request.From),
						new FilterFieldLesserOrEqualTo("Timestamp", Request.To)),
						"Timestamp"))
					{
						if (Fields.Count > 50)
						{
							Request.ReportFields(false, Fields);
							Fields.Clear();
						}

						if (Rec.AvgLight.HasValue)
						{
							Fields.Add(new QuantityField(this, Rec.Timestamp, "Light, Minute, Average",
								Rec.AvgLight.Value, 2, "%", FieldType.Computed | FieldType.Historical, FieldQoS.AutomaticReadout));
						}

						if (Rec.AvgMotion.HasValue)
						{
							Fields.Add(new QuantityField(this, Rec.Timestamp, "Motion, Minute, Average",
								Rec.AvgMotion.Value, 2, "%", FieldType.Computed | FieldType.Historical, FieldQoS.AutomaticReadout));
						}

						if (Rec.MinLight.HasValue)
						{
							Fields.Add(new QuantityField(this, Rec.MinLightAt, "Light, Minute, Minimum",
								Rec.MinLight.Value, 2, "%", FieldType.Computed | FieldType.Historical, FieldQoS.AutomaticReadout));
						}

						if (Rec.MaxLight.HasValue)
						{
							Fields.Add(new QuantityField(this, Rec.MaxLightAt, "Light, Minute, Maximum",
								Rec.MaxLight.Value, 2, "%", FieldType.Computed | FieldType.Historical, FieldQoS.AutomaticReadout));
						}
					}

					if (Fields.Count > 0)
					{
						Request.ReportFields(false, Fields);
						Fields.Clear();
					}

					foreach (LastHour Rec in await Database.Find<LastHour>(new FilterAnd(
						new FilterFieldGreaterOrEqualTo("Timestamp", Request.From),
						new FilterFieldLesserOrEqualTo("Timestamp", Request.To)),
						"Timestamp"))
					{
						if (Fields.Count > 50)
						{
							Request.ReportFields(false, Fields);
							Fields.Clear();
						}

						if (Rec.AvgLight.HasValue)
						{
							Fields.Add(new QuantityField(this, Rec.Timestamp, "Light, Hour, Average",
								Rec.AvgLight.Value, 2, "%", FieldType.Computed | FieldType.Historical, FieldQoS.AutomaticReadout));
						}

						if (Rec.AvgMotion.HasValue)
						{
							Fields.Add(new QuantityField(this, Rec.Timestamp, "Motion, Hour, Average",
								Rec.AvgMotion.Value, 2, "%", FieldType.Computed | FieldType.Historical, FieldQoS.AutomaticReadout));
						}

						if (Rec.MinLight.HasValue)
						{
							Fields.Add(new QuantityField(this, Rec.MinLightAt, "Light, Hour, Minimum",
								Rec.MinLight.Value, 2, "%", FieldType.Computed | FieldType.Historical, FieldQoS.AutomaticReadout));
						}

						if (Rec.MaxLight.HasValue)
						{
							Fields.Add(new QuantityField(this, Rec.MaxLightAt, "Light, Hour, Maximum",
								Rec.MaxLight.Value, 2, "%", FieldType.Computed | FieldType.Historical, FieldQoS.AutomaticReadout));
						}
					}

					foreach (LastDay Rec in await Database.Find<LastDay>(new FilterAnd(
						new FilterFieldGreaterOrEqualTo("Timestamp", Request.From),
						new FilterFieldLesserOrEqualTo("Timestamp", Request.To)),
						"Timestamp"))
					{
						if (Fields.Count > 50)
						{
							Request.ReportFields(false, Fields);
							Fields.Clear();
						}

						if (Rec.AvgLight.HasValue)
						{
							Fields.Add(new QuantityField(this, Rec.Timestamp, "Light, Day, Average",
								Rec.AvgLight.Value, 2, "%", FieldType.Computed | FieldType.Historical, FieldQoS.AutomaticReadout));
						}

						if (Rec.AvgMotion.HasValue)
						{
							Fields.Add(new QuantityField(this, Rec.Timestamp, "Motion, Day, Average",
								Rec.AvgMotion.Value, 2, "%", FieldType.Computed | FieldType.Historical, FieldQoS.AutomaticReadout));
						}

						if (Rec.MinLight.HasValue)
						{
							Fields.Add(new QuantityField(this, Rec.MinLightAt, "Light, Day, Minimum",
								Rec.MinLight.Value, 2, "%", FieldType.Computed | FieldType.Historical, FieldQoS.AutomaticReadout));
						}

						if (Rec.MaxLight.HasValue)
						{
							Fields.Add(new QuantityField(this, Rec.MaxLightAt, "Light, Day, Maximum",
								Rec.MaxLight.Value, 2, "%", FieldType.Computed | FieldType.Historical, FieldQoS.AutomaticReadout));
						}
					}
				}

				Request.ReportFields(true, Fields);
			}
			catch (Exception ex)
			{
				Log.Critical(ex);
			}
		}
	}
}
