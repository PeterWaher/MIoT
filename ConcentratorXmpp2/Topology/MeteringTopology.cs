using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Waher.Runtime.Language;
using Waher.Things;
using Waher.Things.SourceEvents;

namespace ConcentratorXmpp.Topology
{
	public class MeteringTopology : IDataSource
	{
		public const string ID = "MeteringTopology";

		public static ActuatorNode ActuatorNode = new ActuatorNode();
		public static SensorNode SensorNode = new SensorNode();

		public MeteringTopology()
		{
		}

		public string SourceID => ID;
		public bool HasChildren => false;
		public DateTime LastChanged => DateTime.MinValue;
		public IEnumerable<IDataSource> ChildSources => null;

		public IEnumerable<INode> RootNodes => new INode[] { ActuatorNode, SensorNode };

		public event SourceEventEventHandler OnEvent;

		public Task<bool> CanViewAsync(RequestOrigin Caller)
		{
			return Task.FromResult<bool>(true);
		}

		public Task<string> GetNameAsync(Language Language)
		{
			return Language.GetStringAsync(typeof(MeteringTopology), 1, "Metering Topology");
		}

		public Task<INode> GetNodeAsync(IThingReference NodeRef)
		{
			if (SensorNode.SameThing(NodeRef))
				return Task.FromResult<INode>(SensorNode);
			else if (ActuatorNode.SameThing(NodeRef))
				return Task.FromResult<INode>(ActuatorNode);
			else
				return Task.FromResult<INode>(null);
		}
	}
}
