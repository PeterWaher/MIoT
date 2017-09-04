using Waher.Networking.LWM2M;

namespace SensorLwm2m.IPSO
{
	public class DigitalInput : Lwm2mObject
	{
		public DigitalInput(params DigitalInputInstance[] Inputs)
			: base(3200, Inputs)
		{
		}

		public DigitalInput(ushort Id, params DigitalInputInstance[] Inputs)
			: base(Id, Inputs)
		{
		}

	}
}
