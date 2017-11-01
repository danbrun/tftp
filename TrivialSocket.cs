using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace tftp
{
	class TrivialSocket
	{
		static readonly byte[] octet = Encoding.ASCII.GetBytes("octet");
		static readonly byte[] zero = { 0 };

		string host;
		bool errors = false;
		Socket socket;
		IPEndPoint dest;

		//
		// Constructs a new TFTP socket
		//
		// @param host The host to download from
		// @param errors Whether or not to request with errors
		public TrivialSocket(string host, bool errors)
		{
			this.host = host;
			this.errors = errors;
		}

		//
		// Downloads the file from the host
		//
		// @param file The name of the file to download
		public void Download(string file)
		{
			RRQ(file);

			while (true)
			{
				var (size, block, error) = DATA();

				Console.WriteLine("Read block {0}", block);

				if (error)
				{
					NACK(block);
					continue;
				}

				if (size < 516)
				{
					return;
				}

				ACK(block);
			}
		}

		//
		// Requests a file by sending an RRQ packet
		//
		// @param file The file to request
		void RRQ(string file)
		{
			dest = new IPEndPoint(Dns.GetHostAddresses(host)[0], 7000);
			socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

			var opcode = new byte[] { 0, 1 };
			var name = Encoding.ASCII.GetBytes(file);

			var data = Program.combine<byte>(opcode, name, zero, octet, zero);
			socket.SendTo(data, dest);
		}

		//
		// Reads a UDP packet from the server
		//
		// @returns A tuple with the number of bytes read, block number, and error flag
		(int size, ushort block, bool error) DATA()
		{
			var data = new byte[516];
			var length = socket.Receive(data);
			var block = (ushort)((data[2] << 8) + data[3]);
			return (length, block, false);
		}

		//
		// Sends an acknowledgement in an ACK packet
		//
		// @param block The number of the block received
		void ACK(ushort block)
		{
			var data = new byte[] { 0, 4, (byte)(block >> 8), (byte)block };
			socket.SendTo(data, dest);

			foreach (var a in data)
			{
				Console.Write(a + " ");
			}
			Console.WriteLine();
		}

		//
		// Sends an error acknowledgement as a NACK
		//
		// @param block The number of the block to redownload
		void NACK(ushort block)
		{
			var data = new byte[] { 0, 6, (byte)(block >> 8), (byte)block };
			socket.SendTo(data, dest);
		}
	}
}
