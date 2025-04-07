using System.Text;
using System.Threading.Tasks;
using Waher.Events;
using Waher.Networking.Sniffers;
using Waher.Networking.Sniffers.Model;

namespace ActuatorHttp
{
	public class LogSniffer : SnifferBase
	{
		public override BinaryPresentationMethod BinaryPresentationMethod => BinaryPresentationMethod.Hexadecimal;

		public override Task Process(SnifferError Event)
		{
			Log.Error(Event.Text);
			return Task.CompletedTask;
		}

		public override Task Process(SnifferException Event)
		{
			Log.Critical(Event.Text);
			return Task.CompletedTask;
		}

		public override Task Process(SnifferInformation Event)
		{
			Log.Informational(Event.Text);
			return Task.CompletedTask;
		}

		public override Task Process(SnifferRxBinary Event)
		{
			Log.Informational("Rx: " + ToString(Event.Data, Event.Offset, Event.Count));
			return Task.CompletedTask;
		}

		public override Task Process(SnifferRxText Event)
		{
			Log.Informational("Rx: " + Event.Text);
			return Task.CompletedTask;
		}

		public override Task Process(SnifferTxBinary Event)
		{
			Log.Informational("Tx: " + ToString(Event.Data, Event.Offset, Event.Count));
			return Task.CompletedTask;
		}

		public override Task Process(SnifferTxText Event)
		{
			Log.Informational("Tx: " + Event.Text);
			return Task.CompletedTask;
		}

		public override Task Process(SnifferWarning Event)
		{
			Log.Warning(Event.Text);
			return Task.CompletedTask;
		}

		private static string ToString(byte[] Data, int Offset, int Count)
		{
			StringBuilder sb = new StringBuilder();
			bool First = true;

			while (Count-- > 0)
			{
				byte b = Data[Offset++];

				if (First)
					First = false;
				else
					sb.Append(' ');

				sb.Append(b.ToString("X2"));
			}

			return sb.ToString();
		}
	}
}
