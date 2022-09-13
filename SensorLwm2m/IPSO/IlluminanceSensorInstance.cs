namespace SensorLwm2m.IPSO
{
	public class IlluminanceSensorInstance : GenericSensorInstance
	{
		public IlluminanceSensorInstance(ushort InstanceId, double? CurrentValue, string Unit, double MinRange, double MaxRange)
			: base(3301, InstanceId, CurrentValue, Unit, MinRange, MaxRange, null, null)
		{
		}
	}
}
