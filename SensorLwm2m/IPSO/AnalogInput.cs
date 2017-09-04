using Waher.Networking.LWM2M;

namespace SensorLwm2m.IPSO
{
	public class AnalogInput : Lwm2mObject
	{
		public AnalogInput(params AnalogInputInstance[] Inputs)
			: base(3202, Inputs)
		{
		}
	}
}
