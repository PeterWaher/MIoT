using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Waher.Runtime.Language;
using Waher.Things;
using Waher.Things.DisplayableParameters;

namespace ConcentratorXmpp.Topology
{
	public class SensorNode : INode
	{
		public SensorNode()
		{
		}

		public string LocalId => "Sensor";
		public string LogId => "Sensor";
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
		public string NodeId => "Sensor";
		public string SourceId => MeteringTopology.ID;
		public string Partition => string.Empty;

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
	}
}
