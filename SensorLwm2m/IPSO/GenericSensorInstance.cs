using System;
using System.Threading.Tasks;
using Waher.Networking.LWM2M;

namespace SensorLwm2m.IPSO
{
	public class GenericSensorInstance : Lwm2mObjectInstance
	{
		private readonly Lwm2mResourceDouble current;
		private readonly Lwm2mResourceString unit;
		private readonly Lwm2mResourceDouble min;
		private readonly Lwm2mResourceDouble max;
		private readonly Lwm2mResourceDouble minRange;
		private readonly Lwm2mResourceDouble maxRange;
		private readonly Lwm2mResourceCommand resetMinMax;
		private readonly Lwm2mResourceString applicationType;
		private readonly Lwm2mResourceString sensorType;

		public GenericSensorInstance(ushort InstanceId, double? CurrentValue, string Unit, double MinRange, double MaxRange, string ApplicationType, string SensorType)
			: this(3300, InstanceId, CurrentValue, Unit, MinRange, MaxRange, ApplicationType, SensorType)
		{
		}

		public GenericSensorInstance(ushort ObjectInstanceId, ushort InstanceId, double? CurrentValue, string Unit, double MinRange, double MaxRange, string ApplicationType, string SensorType)
			: base(ObjectInstanceId, InstanceId)
		{
			this.current = new Lwm2mResourceDouble("Sensor Value", ObjectInstanceId, InstanceId, 5700, false, false, CurrentValue);
			this.unit = new Lwm2mResourceString("Unit", ObjectInstanceId, InstanceId, 5701, false, false, Unit);
			this.min = new Lwm2mResourceDouble("Min Measured Value", ObjectInstanceId, InstanceId, 5601, false, false, CurrentValue);
			this.max = new Lwm2mResourceDouble("Max Measured Value", ObjectInstanceId, InstanceId, 5602, false, false, CurrentValue);
			this.minRange = new Lwm2mResourceDouble("Min Range Value", ObjectInstanceId, InstanceId, 5603, false, false, MinRange);
			this.maxRange = new Lwm2mResourceDouble("Max Range Value", ObjectInstanceId, InstanceId, 5604, false, false, MaxRange);
			this.resetMinMax = new Lwm2mResourceCommand("Reset Min and Max Measured Values", ObjectInstanceId, InstanceId, 5605);

			if (ApplicationType != null)
				this.applicationType = new Lwm2mResourceString("Application Type", ObjectInstanceId, InstanceId, 5750, true, true, ApplicationType);

			if (SensorType != null)
				this.sensorType = new Lwm2mResourceString("Sensor Type", ObjectInstanceId, InstanceId, 5751, false, false, SensorType);

			this.resetMinMax.OnExecute += (sender, e) =>
			{
				this.min.DoubleValue = this.current.DoubleValue;
				this.min.TriggerAll();

				this.max.DoubleValue = this.current.DoubleValue;
				this.max.TriggerAll();

				this.TriggerAll();
			
				return Task.CompletedTask;
			};

			this.Add(this.current);
			this.Add(this.unit);
			this.Add(this.min);
			this.Add(this.max);
			this.Add(this.minRange);
			this.Add(this.maxRange);
			this.Add(this.resetMinMax);

			if (this.applicationType != null)
				this.Add(this.applicationType);

			if (this.sensorType != null)
				this.Add(this.sensorType);
		}

		public override async Task AfterRegister(Lwm2mClient Client)
		{
			await base.AfterRegister(Client);

			await this.TriggerAll(new TimeSpan(0, 1, 0));
			await this.current.TriggerAll(new TimeSpan(0, 1, 0));
			await this.min.TriggerAll(new TimeSpan(0, 1, 0));
			await this.max.TriggerAll(new TimeSpan(0, 1, 0));
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
