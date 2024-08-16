using System;
using System.Threading.Tasks;
using Waher.Events;
using Waher.Networking.LWM2M;
using Waher.Networking.LWM2M.Events;

namespace ActuatorLwm2m.IPSO
{
	public class DigitalOutputInstance : Lwm2mObjectInstance
	{
		private readonly Lwm2mResourceBoolean state;
		private readonly Lwm2mResourceString applicationType;

		public DigitalOutputInstance(ushort InstanceId, bool? CurrentState, string ApplicationType)
			: this(3201, InstanceId, CurrentState, ApplicationType)
		{
		}

		public DigitalOutputInstance(ushort ObjectInstanceId, ushort InstanceId, bool? CurrentState, string ApplicationType)
			: base(ObjectInstanceId, InstanceId)
		{
			this.state = new Lwm2mResourceBoolean("Digital Output State", ObjectInstanceId, InstanceId, 5550, true, false, CurrentState);

			if (ApplicationType != null)
				this.applicationType = new Lwm2mResourceString("Application Type", ObjectInstanceId, InstanceId, 5750, true, true, ApplicationType);

			this.state.OnRemoteUpdate += (sender, e) =>
			{
				this.state.TriggerAll();
				this.TriggerAll();

				try
				{
					this.OnRemoteUpdate?.Invoke(this, e);
				}
				catch (Exception ex)
				{
					Log.Exception(ex);
				}
			
				return Task.CompletedTask;
			};

			this.Add(this.state);

			if (this.applicationType != null)
				this.Add(this.applicationType);
		}

		public event CoapRequestEventHandler OnRemoteUpdate = null;

		public bool Value
		{
			get { return this.state.BooleanValue.HasValue && this.state.BooleanValue.Value; }
		}

		public override void AfterRegister(Lwm2mClient Client)
		{
			base.AfterRegister(Client);

			this.TriggerAll(new TimeSpan(0, 1, 0));
			this.state.TriggerAll(new TimeSpan(0, 1, 0));
		}

		public void Set(bool Value)
		{
			if (!this.state.BooleanValue.HasValue ||
				this.state.BooleanValue.Value != Value)
			{
				this.state.BooleanValue = Value;
				this.state.TriggerAll();
				this.TriggerAll();
			}
		}

	}
}
