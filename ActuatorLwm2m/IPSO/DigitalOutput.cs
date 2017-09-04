using Waher.Networking.LWM2M;

namespace ActuatorLwm2m.IPSO
{
	public class DigitalOutput : Lwm2mObject
	{
		public DigitalOutput(params DigitalOutputInstance[] Inputs)
			: base(3201, Inputs)
		{
		}

		public DigitalOutput(ushort Id, params DigitalOutputInstance[] Inputs)
			: base(Id, Inputs)
		{
		}

	}
}
