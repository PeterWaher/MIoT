using System;
using Waher.Networking.LWM2M;

namespace SensorLwm2m.IPSO
{
	public class AnalogInputInstance : Lwm2mObjectInstance
	{
		private Lwm2mResourceDouble current;
		private Lwm2mResourceDouble min;
		private Lwm2mResourceDouble max;
		private Lwm2mResourceDouble minRange;
		private Lwm2mResourceDouble maxRange;
		private Lwm2mResourceCommand resetMinMax;
		private Lwm2mResourceString applicationType;
		private Lwm2mResourceString sensorType;

		public AnalogInputInstance(ushort InstanceId, double? CurrentValue, double MinRange, double MaxRange, string ApplicationType, string SensorType)
			: base(3202, InstanceId)
		{
			this.current = new Lwm2mResourceDouble("Analog Input Current Value", 3202, InstanceId, 5600, false, false, CurrentValue);
			this.min = new Lwm2mResourceDouble("Min Measured Value", 3202, InstanceId, 5601, false, false, CurrentValue);
			this.max = new Lwm2mResourceDouble("Max Measured Value", 3202, InstanceId, 5602, false, false, CurrentValue);
			this.minRange = new Lwm2mResourceDouble("Min Range Value", 3202, InstanceId, 5603, false, false, MinRange);
			this.maxRange = new Lwm2mResourceDouble("Max Range Value", 3202, InstanceId, 5604, false, false, MaxRange);
			this.resetMinMax = new Lwm2mResourceCommand("Reset Min and Max Measured Values", 3202, InstanceId, 5605);
			this.applicationType = new Lwm2mResourceString("Application Type", 3202, InstanceId, 5750, true, true, ApplicationType);
			this.sensorType = new Lwm2mResourceString("Sensor Type", 3202, InstanceId, 5751, false, false, SensorType);

			this.resetMinMax.OnExecute += (sender, e) =>
			{
				this.min.DoubleValue = this.current.DoubleValue;
				this.min.TriggerAll();

				this.max.DoubleValue = this.current.DoubleValue;
				this.max.TriggerAll();

				this.TriggerAll();
			};

			this.Add(this.current);
			this.Add(this.min);
			this.Add(this.max);
			this.Add(this.minRange);
			this.Add(this.maxRange);
			this.Add(this.resetMinMax);
			this.Add(this.applicationType);
			this.Add(this.sensorType);
		}

		public override void AfterRegister(Lwm2mClient Client)
		{
			base.AfterRegister(Client);

			this.TriggerAll(new TimeSpan(0, 1, 0));
			this.current.TriggerAll(new TimeSpan(0, 1, 0));
			this.min.TriggerAll(new TimeSpan(0, 1, 0));
			this.max.TriggerAll(new TimeSpan(0, 1, 0));
		}

		public void Set(double Value)
		{
			if (!this.current.DoubleValue.HasValue ||
				this.current.DoubleValue.Value != Value)
			{
				this.current.DoubleValue = Value;
				this.current.TriggerAll();

				if (!this.min.DoubleValue.HasValue || Value < this.min.DoubleValue.Value)
				{
					this.min.DoubleValue = Value;
					this.min.TriggerAll();
				}

				if (!this.max.DoubleValue.HasValue || Value > this.max.DoubleValue.Value)
				{
					this.max.DoubleValue = Value;
					this.max.TriggerAll();
				}

				this.TriggerAll();
			}
		}

	}
}
