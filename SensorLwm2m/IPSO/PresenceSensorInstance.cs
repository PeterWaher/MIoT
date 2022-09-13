namespace SensorLwm2m.IPSO
{
	public class PresenceSensorInstance : DigitalInputInstance
	{
		public PresenceSensorInstance(ushort InstanceId, bool? CurrentState, string SensorType)
			: base(3302, InstanceId, CurrentState, null, SensorType)
		{
		}
	}
}
