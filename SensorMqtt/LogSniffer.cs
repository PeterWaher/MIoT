using System;
using Waher.Events;
using Waher.Networking;
using Waher.Networking.Sniffers;

namespace SensorMqtt
{
	public class LogSniffer : ISniffer
	{
		public void Error(string Error)
		{
			Log.Error(Error);
		}

		public void Exception(string Exception)
		{
			Log.Critical(Exception);
		}

		public void Information(string Comment)
		{
			Log.Informational(Comment);
		}

		public void ReceiveBinary(byte[] Data)
		{
			Log.Informational("Rx: " + Hashes.BinaryToString(Data));
		}

		public void ReceiveText(string Text)
		{
			Log.Informational("Rx: " + Text);
		}

		public void TransmitBinary(byte[] Data)
		{
			Log.Informational("Tx: " + Hashes.BinaryToString(Data));
		}

		public void TransmitText(string Text)
		{
			Log.Informational("Tx: " + Text);
		}

		public void Warning(string Warning)
		{
			Log.Warning(Warning);
		}
	}
}
