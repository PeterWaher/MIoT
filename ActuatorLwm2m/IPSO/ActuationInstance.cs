using System;
using Waher.Events;
using Waher.Networking.LWM2M;
using Waher.Networking.LWM2M.Events;

namespace ActuatorLwm2m.IPSO
{
	public class ActuationInstance : Lwm2mObjectInstance
	{
		private Lwm2mResourceBoolean onOff;
		private Lwm2mResourceString applicationType;
		private Lwm2mResourceInteger onTime;
		private DateTime lastSet = DateTime.Now;

		public ActuationInstance(ushort InstanceId, bool? CurrentState, string ApplicationType)
			: this(3306, InstanceId, CurrentState, ApplicationType)
		{
		}

		public ActuationInstance(ushort ObjectInstanceId, ushort InstanceId, bool? CurrentState, string ApplicationType)
			: base(ObjectInstanceId, InstanceId)
		{
			this.onOff = new Lwm2mResourceBoolean("On/Off", ObjectInstanceId, InstanceId, 5850, true, false, CurrentState);
			this.onTime = new Lwm2mResourceInteger("On Time", ObjectInstanceId, InstanceId, 5852, true, false, 0, false);

			if (ApplicationType != null)
				this.applicationType = new Lwm2mResourceString("Application Type", ObjectInstanceId, InstanceId, 5750, true, true, ApplicationType);

			this.onOff.OnRemoteUpdate += (sender, e) =>
			{
				this.onOff.TriggerAll();
				this.TriggerAll();

				try
				{
					this.OnRemoteUpdate?.Invoke(this, e);
				}
				catch (Exception ex)
				{
					Log.Critical(ex);
				}
			};

			this.onTime.OnBeforeGet += (sender, e) =>
			{
				if (this.onOff.BooleanValue.HasValue && this.onOff.BooleanValue.Value)
					this.onTime.IntegerValue = (long)((DateTime.Now - this.lastSet).TotalSeconds + 0.5);
				else
					this.onTime.IntegerValue = 0;
			};

			this.onTime.OnRemoteUpdate += (sender, e) =>
			{
				if (this.onTime.IntegerValue.HasValue)
					this.lastSet = DateTime.Now.AddSeconds(-this.onTime.IntegerValue.Value);
				else
					this.lastSet = DateTime.Now;
			};

			this.Add(this.onOff);
			this.Add(this.onTime);

			if (this.applicationType != null)
				this.Add(this.applicationType);
		}

		public event CoapRequestEventHandler OnRemoteUpdate = null;

		public bool Value
		{
			get { return this.onOff.BooleanValue.HasValue && this.onOff.BooleanValue.Value; }
		}

		public override void AfterRegister(Lwm2mClient Client)
		{
			base.AfterRegister(Client);

			this.TriggerAll(new TimeSpan(0, 1, 0));
			this.onOff.TriggerAll(new TimeSpan(0, 1, 0));
			this.onTime.TriggerAll(new TimeSpan(0, 0, 1));
		}

		public void Set(bool Value)
		{
			if (!this.onOff.BooleanValue.HasValue ||
				this.onOff.BooleanValue.Value != Value)
			{
				this.lastSet = DateTime.Now;
				this.onOff.BooleanValue = Value;
				this.onOff.TriggerAll();
				this.TriggerAll();
			}
		}

	}
}
