using System;
using System.Threading.Tasks;
using Waher.Networking.LWM2M;

namespace SensorLwm2m.IPSO
{
	public class DigitalInputInstance : Lwm2mObjectInstance
	{
		private readonly Lwm2mResourceBoolean state;
		private readonly Lwm2mResourceInteger counter;
		private readonly Lwm2mResourceCommand counterReset;
		private readonly Lwm2mResourceString applicationType;
		private readonly Lwm2mResourceString sensorType;

		public DigitalInputInstance(ushort InstanceId, bool? CurrentState, string ApplicationType, string SensorType)
			: this(3200, InstanceId, CurrentState, ApplicationType, SensorType)
		{
		}

		public DigitalInputInstance(ushort ObjectInstanceId, ushort InstanceId, bool? CurrentState, string ApplicationType, string SensorType)
			: base(ObjectInstanceId, InstanceId)
		{
			this.state = new Lwm2mResourceBoolean("Digital Input State", ObjectInstanceId, InstanceId, 5500, false, false, CurrentState);
			this.counter = new Lwm2mResourceInteger("Digital Input Counter", ObjectInstanceId, InstanceId, 5501, false, false, 0, false);
			this.counterReset = new Lwm2mResourceCommand("Digital Input Counter Reset", ObjectInstanceId, InstanceId, 5505);

			if (ApplicationType != null)
				this.applicationType = new Lwm2mResourceString("Application Type", ObjectInstanceId, InstanceId, 5750, true, true, ApplicationType);

			if (SensorType != null)
				this.sensorType = new Lwm2mResourceString("Sensor Type", ObjectInstanceId, InstanceId, 5751, false, false, SensorType);

			this.counterReset.OnExecute += (sender, e) =>
			{
				this.counter.IntegerValue = 0;
				this.counter.TriggerAll();
				this.TriggerAll();
			
				return Task.CompletedTask;
			};

			this.Add(this.state);
			this.Add(this.counter);
			this.Add(this.counterReset);

			if (this.applicationType != null)
				this.Add(this.applicationType);

			if (this.sensorType != null)
				this.Add(this.sensorType);
		}

		public override void AfterRegister(Lwm2mClient Client)
		{
			base.AfterRegister(Client);

			this.TriggerAll(new TimeSpan(0, 1, 0));
			this.state.TriggerAll(new TimeSpan(0, 1, 0));
			this.counter.TriggerAll(new TimeSpan(0, 1, 0));
		}

		public void Set(bool Value)
		{
			if (!this.state.BooleanValue.HasValue ||
				this.state.BooleanValue.Value != Value)
			{
				this.state.BooleanValue = Value;
				this.state.TriggerAll();

				if (Value)
				{
					this.counter.IntegerValue++;
					this.counter.TriggerAll();
				}

				this.TriggerAll();
			}
		}

	}
}
