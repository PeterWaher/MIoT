namespace SensorLwm2m.IPSO
{
	public class PercentageSensorInstance : GenericSensorInstance
	{
		public PercentageSensorInstance(ushort InstanceId, double? CurrentValue, double MinRange, double MaxRange, string ApplicationType)
			: base(3320, InstanceId, CurrentValue, "%", MinRange, MaxRange, ApplicationType, null)
		{
		}
	}
}
