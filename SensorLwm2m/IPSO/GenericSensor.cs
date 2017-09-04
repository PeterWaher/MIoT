using Waher.Networking.LWM2M;

namespace SensorLwm2m.IPSO
{
	public class GenericSensor : Lwm2mObject
	{
		public GenericSensor(params GenericSensorInstance[] Inputs)
			: base(3300, Inputs)
		{
		}

		public GenericSensor(ushort Id, params GenericSensorInstance[] Inputs)
			: base(Id, Inputs)
		{
		}
	}
}
