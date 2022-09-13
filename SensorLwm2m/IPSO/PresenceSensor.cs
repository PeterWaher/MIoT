namespace SensorLwm2m.IPSO
{
	public class PresenceSensor : DigitalInput
	{
		public PresenceSensor(params PresenceSensorInstance[] Inputs)
			: base(3302, Inputs)
		{
		}
	}
}
