using Waher.Networking.LWM2M;

namespace SensorLwm2m.IPSO
{
	public class IlluminanceSensor : Lwm2mObject
	{
		public IlluminanceSensor(params IlluminanceSensorInstance[] Inputs)
			: base(3301, Inputs)
		{
		}
	}
}
