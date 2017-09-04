using System.Collections.Generic;
using System.Text;
using Waher.Events;

namespace SensorLwm2m
{
	public class Events : EventSink
	{
		public Events() : base(string.Empty)
		{
		}

		public override void Queue(Event Event)
		{
			StringBuilder sb = new StringBuilder(Event.Message);

			if (!string.IsNullOrEmpty(Event.Object))
			{
				sb.Append(' ');
				sb.Append(Event.Object);
			}

			if (!string.IsNullOrEmpty(Event.Actor))
			{
				sb.Append(' ');
				sb.Append(Event.Actor);
			}

			foreach (KeyValuePair<string, object> Parameter in Event.Tags)
			{
				sb.Append(" [");
				sb.Append(Parameter.Key);
				sb.Append("=");
				if (Parameter.Value != null)
					sb.Append(Parameter.Value.ToString());
				sb.Append("]");
			}

			if (Event.Type >= EventType.Critical && !string.IsNullOrEmpty(Event.StackTrace))
			{
				sb.Append("\r\n\r\n");
				sb.Append(Event.StackTrace);
			}

			MainPage.Instance.AddLogMessage(sb.ToString());
		}
	}
}
