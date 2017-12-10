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
using Waher.Things.ControlParameters;

namespace ConcentratorXmpp.Topology
{
	public class ActuatorNode : ThingReference, ISensor, IActuator
	{
		public const string NodeID = "Actuator";

		public ActuatorNode()
			: base(NodeID, MeteringTopology.ID, string.Empty)
		{
		}

		public string LocalId => this.NodeId;
		public string LogId => this.NodeId;
		public bool HasChildren => false;
		public bool ChildrenOrdered => false;
		public bool IsReadable => true;
		public bool IsControllable => true;
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

		public ControlParameter[] GetControlParameters()
		{
			return new ControlParameter[]
			{
				new BooleanControlParameter("Output", "Actuator", "Output:", "Digital output.",
					(Node) => App.Instance.Output,
					async (Node, Value) =>
					{
						try
						{
							await App.Instance.SetOutput(Value, "XMPP");
						}
						catch (Exception ex)
						{
							Log.Critical(ex);
						}
					})
			};
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
			return Language.GetStringAsync(typeof(MeteringTopology), 5, "Actuator Node");
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

		public void StartReadout(ISensorReadout Request)
		{
			try
			{
				Log.Informational("Performing readout.", App.Instance.XmppClient.BareJID, Request.Actor);

				List<Field> Fields = new List<Field>();
				DateTime Now = DateTime.Now;

				if (Request.IsIncluded(FieldType.Identity))
					Fields.Add(new StringField(this, Now, "Device ID", App.Instance.DeviceId, FieldType.Identity, FieldQoS.AutomaticReadout));

				if (App.Instance.Output.HasValue)
				{
					Fields.Add(new BooleanField(this, Now, "Output", App.Instance.Output.Value,
						FieldType.Momentary, FieldQoS.AutomaticReadout, true));
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
