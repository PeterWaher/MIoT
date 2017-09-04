using Waher.Networking.LWM2M;

namespace SensorLwm2m.IPSO
{
	public class PercentageSensor : Lwm2mObject
	{
		public PercentageSensor(params PercentageSensorInstance[] Inputs)
			: base(3320, Inputs)
		{
		}
	}
}
