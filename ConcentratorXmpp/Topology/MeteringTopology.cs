using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Waher.Runtime.Language;
using Waher.Things;

namespace ConcentratorXmpp.Topology
{
	public class MeteringTopology : IDataSource
	{
		public const string ID = "MeteringTopology";

		private static SensorNode sensorNode = new SensorNode();

		public MeteringTopology()
		{
		}

		public string SourceID => ID;
		public bool HasChildren => false;
		public DateTime LastChanged => DateTime.MinValue;
		public IEnumerable<IDataSource> ChildSources => null;

		public IEnumerable<INode> RootNodes => new INode[] { sensorNode };

		public Task<bool> CanViewAsync(RequestOrigin Caller)
		{
			return Task.FromResult<bool>(true);
		}

		public async Task<string> GetNameAsync(Language Language)
		{
			return await Language.GetStringAsync(typeof(MeteringTopology), 1, "Metering Topology");
		}

		public Task<INode> GetNodeAsync(IThingReference NodeRef)
		{
			if (NodeRef == sensorNode)
				return Task.FromResult<INode>(sensorNode);
			else
				return Task.FromResult<INode>(null);
		}
	}
}
