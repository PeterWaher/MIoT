using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Waher.Events;

namespace Actuator
{
	public class Events : EventSink
	{
		public Events() : base(string.Empty)
		{
		}

		public override Task Queue(Event Event)
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

			return Task.CompletedTask;
		}
	}
}
