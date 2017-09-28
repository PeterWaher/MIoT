using Microsoft.Maker.RemoteWiring;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Waher.Persistence.Attributes;

namespace SensorMqtt.History
{
	[TypeName(TypeNameSerialization.None)]
	[Index("Timestamp")]
	public abstract class Base
	{
		[ObjectId]
		public string ObjectId = null;

		[DefaultValueDateTimeMinValue]
		public DateTime Timestamp = DateTime.MinValue;

		[DefaultValue(0)]
		public double Light = 0;

		[DefaultValue(PinState.LOW)]
		public PinState Motion = PinState.LOW;

		[DefaultValueNull]
		public double? MinLight = null;

		[DefaultValueDateTimeMinValue]
		public DateTime MinLightAt = DateTime.MinValue;

		[DefaultValueNull]
		public double? MaxLight = null;

		[DefaultValueDateTimeMinValue]
		public DateTime MaxLightAt = DateTime.MinValue;

		[DefaultValueNull]
		public double? AvgLight = null;

		[DefaultValueNull]
		public double? AvgMotion = null;
	}
}
