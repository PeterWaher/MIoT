using Waher.Networking.LWM2M;

namespace ActuatorLwm2m.IPSO
{
	public class Actuation : Lwm2mObject
	{
		public Actuation(params ActuationInstance[] Inputs)
			: base(3306, Inputs)
		{
		}
	}
}
